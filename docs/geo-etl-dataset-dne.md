# ETL do Geo — ingestão do dataset DNE

Guia operacional da carga de reference data do módulo `Geo` (localidades, endereçamento e georreferência nacional) a partir do dataset **DNE Correios + IBGE**. Decisões em [ADR-0090](adrs/0090-modulo-geo-localidades.md), [ADR-0091](adrs/0091-postgis-georreferencia-nts.md), [ADR-0092](adrs/0092-etl-carga-dne-reference-data.md) e [ADR-0097](adrs/0097-nome-do-municipio-pelo-ibge.md).

## Estratégia: banco de staging dedicado + SELECT streamado

O dataset DNE é distribuído como **15 dumps SQL** (um por tabela, `tbl_cep_{versao}_n_*`; a maior — `logradouro` — tem ~1,4M linhas / ~382 MB). Esses dumps **não são versionados** neste repositório: a base DNE é proprietária (Correios) e exige licença institucional. Os dumps são gerados via Navicat e qualificam o schema explicitamente em todo `CREATE TABLE`/`INSERT` (`"public"."tbl_cep_{versao}_n_*"`) — não há como redirecioná-los para outro schema via `search_path` (issue #12).

As colunas **descritivas** são `varchar` (nomes, siglas, métricas, lat/long como texto — daí o parse tolerante); os **identificadores** das folhas são `int4`: as PKs da fonte (`id_cidade`, `id_distrito`, `id_bairro`) e as FKs internas (`cidade_id`, `distrito_id`, `bairro_id`). O ETL os lê com o acessor tipado (`GetInt32`/`IsDBNull`) e os traduz para os Guid intra-banco; o domínio nunca adota esses inteiros como identidade.

A ingestão usa **dois bancos Postgres distintos** (mesma instância) e dois passos desacoplados:

1. **Provisionamento (fora do ciclo da API):** os dumps são restaurados sem nenhuma alteração no banco **`uniplus_geo_staging`** — dedicado, independente do banco de domínio `uniplus_geo` (issue #12) — via `psql -f`. Como os dumps já apontam para `"public"`, e esse banco não tem mais nada além do dataset bruto, seu `public` **nativo** os recebe tal como o Navicat os gerou, sem `sed`/reescrita de schema. Os dumps são auto-suficientes (`DROP TABLE IF EXISTS` + `CREATE TABLE` + `INSERT`), então re-restaurar é limpo e idempotente.
2. **Carga (serviço de `Geo.Infrastructure`):** o ETL lê o staging (`ConnectionStrings:GeoStagingDb`) por `SELECT` **streamado** (`DbDataReader` — leitura linha-a-linha, uso de memória estável mesmo nas tabelas grandes), aplica **parse tolerante** (`'-'`/vazio → `null`, `'S'`/`'N'` → `bool`, lat/long → `geography(Point,4326)`), resolve as FKs e faz **upsert por chave natural** no banco de domínio `uniplus_geo` (`ConnectionStrings:GeoDb`, schema `public`).

```
dumps .sql  ──psql -f──▶  uniplus_geo_staging.public (15 tabelas varchar, banco dedicado)
                              │  SELECT streamado (DneStagingFonte, GeoStagingDb)
                              ▼
        parse tolerante ─▶ upsert por chave natural ─▶ uniplus_geo.public.* (domínio Uni+, GeoDb)
```

### Por que staging-DB (e não parsear os .sql em C#)

- **Streaming nativo:** o `DbDataReader` do Npgsql resolve os ~382 MB de logradouro sem carregar o arquivo em memória — sem reimplementar um parser de `INSERT` textual (frágil com escaping/acentos/`NULL`).
- **Resolução de FK:** as FKs internas da DNE (`id_cidade`, `distrito_id`, `bairro_id`, inteiros **instáveis entre versões**) ficam disponíveis para JOIN no staging. O domínio nunca usa esses inteiros como identidade — usa Guid v7 (ADR-0032) + chave natural.
- **Banco dedicado, não um schema do domínio (issue #12):** como os dumps já se qualificam para `"public"`, um banco próprio deixa o `public` nativo recebê-los sem qualquer reescrita — zero risco às tabelas/extensões do domínio (PostGIS, `spatial_ref_sys`) e zero `sed`/`search_path` frágil. Role dedicada (`uniplus_geo_staging_app`, diferente de `uniplus_geo_app`) por menor privilégio.
- **Recarga idempotente:** ver abaixo.

## Restauração do staging (dev)

Pré-requisito: o banco `uniplus_geo_staging` (role `uniplus_geo_staging_app`) provisionado por `docker/init-db.sql`. A connection string é `ConnectionStrings:GeoStagingDb` (`appsettings.Development.json` para rodar a API local; valores documentados em `docker/.env.example`).

> `docker/init-db.sql` só roda no **primeiro boot** do volume do Postgres (`/docker-entrypoint-initdb.d`, comportamento padrão da imagem). Quem já tinha a stack rodando antes desta mudança precisa recriar o volume (`docker compose down -v && docker compose up -d postgres`, perde os dados locais existentes) **ou** rodar o provisionamento manualmente, sem perder nada — em **dois** comandos `psql -c` separados: `CREATE DATABASE` não pode rodar dentro de bloco de transação, e múltiplos statements num único `-c` são enviados como uma transação implícita:
> ```bash
> docker exec -it <container_postgres> psql -U "$POSTGRES_USER" -c \
>   "CREATE ROLE uniplus_geo_staging_app LOGIN PASSWORD 'uniplus_dev';"
> docker exec -it <container_postgres> psql -U "$POSTGRES_USER" -c \
>   "CREATE DATABASE uniplus_geo_staging OWNER uniplus_geo_staging_app;"
> ```

```bash
# 1. Descompactar os dumps (ex.: tbl_cep_202609_postgresql.zip, que traz as tabelas tbl_cep_202610_*) num diretório.
# 2. Restaurar cada dump direto no banco de staging — SEM sed, SEM search_path: os
#    dumps já qualificam "public", e esse banco só existe para recebê-los.
GEO_STAGING_CONN="postgresql://uniplus_geo_staging_app:uniplus_dev@localhost:5432/uniplus_geo_staging"
for f in tbl_cep_202610_n_*.sql; do
  psql "$GEO_STAGING_CONN" -f "$f"
done
```

Restaurar de novo (mesma versão ou uma versão nova) é seguro: cada dump começa com `DROP TABLE IF EXISTS`, então o `psql -f` substitui a tabela por completo.

## Disparo do ETL e validação ponta a ponta (dev)

Com o staging restaurado (seção anterior) e a API local no ar (`dotnet run --project src/geo/Unifesspa.Geo.API`), a carga é disparada pelo endpoint admin — exige JWT com role `plataforma-admin` (ver `docs/development.md` §Authentication):

```bash
# 1. Disparar a importação da versão restaurada (retorna 202 + Location do status).
curl -s -X POST http://localhost:5400/api/admin/geo/importacoes \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"Versao":"202610"}'

# 2. Acompanhar até a execução sair de "EmAndamento" (o worker processa em background —
#    poucos minutos para o dataset completo, dominado pelo COPY de ~1,4M logradouros).
curl -s http://localhost:5400/api/admin/geo/importacoes/{id} -H "Authorization: Bearer $TOKEN"

# 3. Validar: um CEP real deve resolver (200), não 404 — sintoma original do #12
#    era justamente uma base vazia por o staging ter sido restaurado no schema errado.
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5400/api/cep/{cep-real-do-dataset}
```

O relatório da execução (passo 2) traz contadores por tabela (lidos/inseridos/atualizados/órfãos/duplicados/degradados) — qualquer tabela com "lidos=0" indica que o staging não tinha os dados esperados (dump não restaurado, versão divergente).

## Carga atômica e em lote das folhas — Distrito, Bairro e Logradouro

Toda a publicação de uma versão — topo (`pais`, `estado`, `cidade`), folhas (`distrito`/`bairro` + faixas, `cep_grande_usuario`, `cep_geral`, `logradouro_complemento` e os ~1,4M `logradouro`), marcação de registros não vigentes e conclusão da execução — executa sob trava consultiva no PostgreSQL e em **uma única transação** sobre o `GeoDbContext`. Se ocorrer qualquer falha, cancelamento ou estouro de `Geo:Etl:DuracaoMaximaDaCarga` (padrão de 1 hora), a transação é revertida por inteiro e a versão anterior publicada permanece intacta.

### Trava da carga e reinício do serviço

- **Ordem do pedido:** o endpoint obtém a trava consultiva do PostgreSQL (`pg_try_advisory_lock`, em conexão própria) antes da guarda de versão e do registro da execução como `EmAndamento`. Com a trava ocupada, responde 409 `em_andamento` sem gravar nada. Recusa posterior (versão anterior à aplicada, conflito de registro, fila fechada) solta a trava.
- **Duração:** a trava acompanha a execução pela fila em memória e só é solta quando a carga termina, depois de marcar o resultado (`Concluida` ou `Falhou`). No desligamento, as cargas ainda na fila são marcadas `Falhou` antes de a trava ser solta.
- **Reinício:** na partida, o serviço tenta obter a mesma trava. Se conseguir, toda execução `EmAndamento` está abandonada (execução viva sempre tem a trava), marca todas como `Falhou` e sincroniza o selo do cache de CEP com a última execução `Concluida`, na mesma seção sob a trava. Se a trava estiver ocupada, outra instância está carregando (por exemplo, durante a implantação com duas instâncias): nada é alterado e o selo fica a cargo de quem detém a trava, que o grava ao confirmar a carga.

O pipeline das folhas (`GeoImportadorLocalidades`) organiza a carga em duas etapas dentro dessa mesma transação:

1. **Upsert (`GeoImportadorDistritoBairro`)** — `distrito`/`bairro` (+faixas), `cep_grande_usuario` e `cep_geral`, por chave natural. O `cep_geral` guarda o CEP oficial de cidade ou distrito, que a DNE entrega como linha de `logradouro` sem nome (tipo e nomes vazios); essas linhas saem do COPY de logradouros e não contam como ignoradas. Chave natural: `cep`. Esta etapa também **resolve as FKs**: monta os dicionários `id_cidade → Cidade.Id (Guid)` (via JOIN do staging com o domínio pelo código IBGE) e `id_distrito`/`id_bairro → Guid` (os ids da DNE são as PKs da fonte, únicos dentro de uma versão; homônimos em cidades distintas viram entidades distintas).
2. **COPY em lote (`ImportadorDeLogradourosPorCopia`)** — `logradouro_complemento` e `logradouro`, via COPY binário streamado sobre a conexão e a transação ativas do `GeoDbContext`.

### Estratégia de carga em lote (cópia → tabela temporária → mesclagem)

A carga segue um caminho único e idempotente (tanto em base vazia quanto em recarga):

```
COPY binário streamado ─▶ tabela temporária (sem restrições)
        │
        ▼
INSERT ... SELECT DISTINCT ON (chave natural) ... ON CONFLICT DO UPDATE  ─▶  tabela final
        │
        ▼
remoção da tabela temporária
```

- **COPY binário** (`NpgsqlBinaryImporter`) em lotes sobre a transação ativa do `GeoDbContext`, com leitura **em streaming** do staging-DB — uso de memória estável mesmo nos 1,4M logradouros. A coordenada é escrita explicitamente como `NpgsqlDbType.Geography` (`geography(Point,4326)`).
- **Dedup intra-fonte** via `DISTINCT ON (chave natural) ... ORDER BY ..., ordem_de_copia DESC` (a última linha vence; `ordem_de_copia` é um `bigserial` preenchido na ordem do COPY) — evita o erro do Postgres "ON CONFLICT cannot affect row a second time".
- **Idempotência** via `ON CONFLICT (chave natural) DO UPDATE`: preserva `id` (Guid v7) e `created_at`, carimba `updated_at` e reativa `vigente = true`. A chave de `logradouro` é `(cep, nome_normalizado, cidade_id)`; a de `logradouro_complemento`, `(cep, complemento_normalizado)`.
- **Auditoria sem interceptor**: como o COPY e a mesclagem ignoram o `AuditableInterceptor`, o `created_at` é carimbado pelo `TimeProvider` (no insert) e o `updated_at` só no `DO UPDATE` — tudo num único instante por carga.
- **Índices preservados na transação única**: os índices de `logradouro` (`gin_trgm` em `nome_normalizado` e GIST em `coordenada`) permanecem ativos durante toda a carga; não há modo inicial separado com `TRUNCATE` nem `CREATE INDEX CONCURRENTLY`.

## Recarga periódica (versões mensais)

A DNE é atualizada mensalmente. Para aplicar uma nova versão (ex.: `202602`):

1. Restaurar o novo dump no banco `uniplus_geo_staging` (substitui o anterior).
2. Disparar a importação pelo endpoint administrativo (`POST /api/admin/geo/importacoes`) informando a versão `202602` em JSON (`{"Versao":"202602"}`).
3. A carga faz **upsert por chave natural** — `sigla_iso`/`uf`/`codigo_ibge` são estáveis entre versões: insere o novo, atualiza o que mudou e grava `versao_dataset=202602` em cada linha tocada.
4. Na mesma transação, registros ausentes na nova versão (ou não revistos na recarga da mesma versão) são marcados com `vigente = false` (`MarcacaoDeNaoVigentes`), sem exclusão física. Após confirmar a transação, o cache de CEP é selado sem garantia de sucesso com o identificador da execução concluída.

## Nome oficial do município (IBGE)

A DNE e o IBGE não escrevem todos os nomes de município do mesmo jeito (86 divergem; 47 por causa do apóstrofo, como «Pau DArco» no lugar de «Pau D'Arco»). O Uni+ usa o nome oficial do IBGE, e a carga o aplica por código ([ADR-0097](adrs/0097-nome-do-municipio-pelo-ibge.md)):

- A lista dos 5.571 municípios (`codigo_ibge`, `uf`, `nome`, separados por tabulação, em UTF-8, ordenada por código, com o texto exato do IBGE) fica em `src/geo/Unifesspa.Geo.Infrastructure/Persistence/Etl/NomesOficiais/municipios-ibge.tsv` e é embutida no assembly. Para cada cidade da DNE, a carga grava o nome da lista pelo código IBGE, com a caixa do IBGE.
- O `nome_normalizado` é derivado do nome gravado (sem acento, caixa nem apóstrofo); a coluna `cidade_sem_acento` da DNE não é mais lida.
- Código que a lista não tem não aborta a carga: o município mantém o nome da DNE e o relatório da tabela `cidade` registra a amostra `nome_oficial_ibge_ausente:{codigo}` (conta como parse degradado).
- Recarregar a mesma versão mantém o nome e o Guid de cada município.

### Como gerar a lista de novo

A lista nunca é editada à mão. Gera-se só com este comando, rodado na raiz do repositório (a extração de 26/11/2026 trouxe 5.571 municípios; nenhum dado do projeto é enviado ao IBGE):

```bash
{ printf 'codigo_ibge\tuf\tnome\n'; \
  curl -fsS 'https://servicodados.ibge.gov.br/api/v1/localidades/municipios?view=nivelado' \
  | jq -r '.[]|[(."municipio-id"|tostring),."UF-sigla",."municipio-nome"]|@tsv' \
  | LC_ALL=C sort; } \
  > src/geo/Unifesspa.Geo.Infrastructure/Persistence/Etl/NomesOficiais/municipios-ibge.tsv
```

Depois de gerar, confira `git diff` (só devem mudar municípios criados ou renomeados pelo IBGE), rode os testes de `NomesOficiaisDosMunicipiosTests` e registre a data da extração na mensagem do commit. Como a lista vai embutida no assembly, atualizá-la exige commit e imagem nova.

### Busca por nome

- Busca de **município**: ignora acento, caixa e apóstrofo (`'`, `’`, `‘`, `´`, `` ` ``, `ʼ`); «pau d'arco», «pau darco» e «PAU D’ARCO» encontram o mesmo município. Pau D'Arco do Pará e do Tocantins são registros distintos, e o filtro `uf` separa os dois.
- Busca de **bairro, distrito, logradouro, grande usuário e complemento**: segue o texto sem acento da DNE, que mantém `´` e `` ` ``; digita-se o termo como a DNE o escreve.

As duas formas existem porque as colunas vêm de fontes diferentes: o nome do município é gerado na carga a partir do nome oficial, e os demais textos vêm prontos da DNE.

### Ambiente que já tem cidades carregadas

Os nomes só mudam quando a carga roda. Ao atualizar a imagem do Geo num ambiente que já tem cidades (HML, produção, base local), o seed não recarrega e a API continua devolvendo os nomes antigos, e a busca pela grafia oficial não encontra o município. Depois de publicar a imagem nova, dispare a importação da mesma versão da DNE (`POST /api/admin/geo/importacoes`, seção anterior) e confira o resultado, por exemplo: `GET /api/cidades/1505551` deve devolver «Pau D'Arco». A recarga mantém o identificador de cada município.

## Parse tolerante (anti-frágil)

A fonte é toda `varchar` e traz `'-'`/vazio para dado ausente (≈27% dos municípios em `mortalidade_infantil`). Toda métrica externa é `nullable` e degrada para `null` sem abortar a carga — só chave natural e `nome` são obrigatórios.

Tipos do domínio por natureza da coluna (a fonte não os distingue):

- **Identificadores → `string`** (preservam zeros à esquerda, sem aritmética): `codigo_ibge`, `cep`/`cep_inicial`/`cep_final`, `ddd`, `uf`/siglas.
- **Métricas inteiras → `int?`**: `populacao_residente(_2022)`, `matriculas_ensino_fundamental_2023`, `rendimento_mensal_per_capita`, `total_veiculos_2023` (chegam inteiros na fonte real, ex.: `'1095'`, `'350273'`).
- **Métricas fracionárias / monetárias → `decimal?`**: `area_territorial_km2`, `densidade_demografica`, `idh`, `escolarizacao_6_a_14_anos`, `mortalidade_infantil`, `receitas`/`despesas` (≈bilhões), `pib_per_capita`. Sempre `InvariantCulture` (ponto decimal); vírgula é **rejeitada** (não vira separador de milhar).
- **`'S'`/`'N'` → `bool`**; **`aniversario` (`DD/MM`) → `string`** (não é data); **lat/long → `geography(Point,4326)`** com validação de domínio (`[-90,90]`/`[-180,180]`). Cada degradação é registrada (sem PII — métricas IBGE públicas) num **relatório de validação** com contadores por tabela (lidos/inseridos/atualizados/órfãos/duplicados/degradados) e amostras para auditoria. A carga só aborta por falha de infraestrutura (conexão/transação), com **rollback total** (transação única).
