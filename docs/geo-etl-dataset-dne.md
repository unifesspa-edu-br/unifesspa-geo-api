# ETL do Geo — ingestão do dataset DNE

Guia operacional da carga de reference data do módulo `Geo` (localidades, endereçamento e georreferência nacional) a partir do dataset **DNE Correios + IBGE**. Decisões em [ADR-0090](adrs/0090-modulo-geo-localidades.md), [ADR-0091](adrs/0091-postgis-georreferencia-nts.md) e [ADR-0092](adrs/0092-etl-carga-dne-reference-data.md).

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
- **Resolução de FK:** as FKs internas da DNE (`id_cidade`, `distrito_id`, `bairro_id`, inteiros **instáveis entre releases**) ficam disponíveis para JOIN no staging. O domínio nunca usa esses inteiros como identidade — usa Guid v7 (ADR-0032) + chave natural.
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
# 1. Descompactar os dumps (ex.: tbl_cep_202601_postgresql.zip) num diretório.
# 2. Restaurar cada dump direto no banco de staging — SEM sed, SEM search_path: os
#    dumps já qualificam "public", e esse banco só existe para recebê-los.
GEO_STAGING_CONN="postgresql://uniplus_geo_staging_app:uniplus_dev@localhost:5432/uniplus_geo_staging"
for f in tbl_cep_202601_n_*.sql; do
  psql "$GEO_STAGING_CONN" -f "$f"
done
```

Restaurar de novo (mesma versão ou uma release nova) é seguro: cada dump começa com `DROP TABLE IF EXISTS`, então o `psql -f` substitui a tabela por completo.

## Disparo do ETL e validação ponta a ponta (dev)

Com o staging restaurado (seção anterior) e a API local no ar (`dotnet run --project src/geo/Unifesspa.Geo.API`), a carga é disparada pelo endpoint admin — exige JWT com role `plataforma-admin` (ver `docs/development.md` §Authentication):

```bash
# 1. Disparar a importação da versão restaurada (retorna 202 + Location do status).
curl -s -X POST http://localhost:5400/api/admin/geo/importacoes \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"Versao":"202601"}'

# 2. Acompanhar até a execução sair de "EmAndamento" (o worker processa em background —
#    poucos minutos para o dataset completo, dominado pelo COPY de ~1,4M logradouros).
curl -s http://localhost:5400/api/admin/geo/importacoes/{id} -H "Authorization: Bearer $TOKEN"

# 3. Validar: um CEP real deve resolver (200), não 404 — sintoma original do #12
#    era justamente uma base vazia por o staging ter sido restaurado no schema errado.
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5400/api/cep/{cep-real-do-dataset}
```

O relatório da execução (passo 2) traz contadores por tabela (lidos/inseridos/atualizados/órfãos/duplicados/degradados) — qualquer tabela com "lidos=0" indica que o staging não tinha os dados esperados (dump não restaurado, versão divergente).

## Carga em lote das folhas — Distrito, Bairro e Logradouro (Story #673)

As folhas da hierarquia (`distrito`/`bairro` + faixas, `cep_grande_usuario`, `cep_geral`, `logradouro_complemento` e os ~1,4M `logradouro`) entram por um pipeline próprio (`GeoImportadorLocalidades`), porque o volume e a criação de índices inviabilizam a transação única do topo (País/Estado/Cidade). O pipeline tem duas fases que **commitam separadamente**:

1. **Upsert (`GeoImportadorDistritoBairro`)** — `distrito`/`bairro` (+faixas), `cep_grande_usuario` e `cep_geral`, por chave natural, numa transação. O `cep_geral` guarda o CEP oficial de cidade ou distrito, que a DNE entrega como linha de `logradouro` sem nome (tipo e nomes vazios); essas linhas saem do COPY de logradouros e não contam como ignoradas. Chave natural: `cep`. Esta fase também **resolve as FKs**: monta os dicionários `id_cidade → Cidade.Id (Guid)` (via JOIN do staging com o domínio pelo código IBGE) e `id_distrito`/`id_bairro → Guid` (os ids da DNE são as PKs da fonte, únicos dentro de uma release; homônimos em cidades distintas viram entidades distintas).
2. **COPY em lote (`LogradouroCopyImporter`)** — `logradouro_complemento` e `logradouro`, via COPY binário streamado.

### Estratégia de bulk (COPY → staging → merge)

Para os dois modos de carga, a estratégia é idêntica e idempotente:

```
COPY binário streamado ─▶ tabela TEMP de staging (sem constraints)
        │
        ▼
INSERT ... SELECT DISTINCT ON (chave natural) ... ON CONFLICT DO UPDATE  ─▶  tabela final
        │
        ▼
DROP da tabela de staging
```

- **COPY binário** (`NpgsqlBinaryImporter`) em lotes, com leitura **em streaming** do staging-DB — uso de memória estável mesmo nos 1,4M logradouros. A coordenada é escrita como `geography(Point,4326)` por um `NpgsqlDataSource` com NetTopologySuite em modo **`geographyAsDefault`**.
- **Dedup intra-fonte** via `DISTINCT ON (chave natural) ... ORDER BY ..., _ord DESC` (last-wins, `_ord` é um `bigserial` preenchido na ordem do COPY) — evita o erro do Postgres "ON CONFLICT cannot affect row a second time".
- **Idempotência** via `ON CONFLICT (chave natural) DO UPDATE`: preserva `id` (Guid v7) e `created_at`, carimba `updated_at` e reativa `vigente = true`. A chave de `logradouro` é `(cep, nome_normalizado, cidade_id)`; a de `logradouro_complemento`, `(cep, complemento_normalizado)`.
- **Auditoria sem interceptor**: como o COPY/merge ignoram o `AuditableInterceptor`, o `created_at` é carimbado pelo `TimeProvider` (no insert) e o `updated_at` só no `DO UPDATE` — tudo num único instante por carga.

### Índices pesados (`logradouro`) e `CREATE INDEX CONCURRENTLY`

No modo **`Inicial`** (base nova), a tabela `logradouro` é truncada e os índices pesados (`gin_trgm` em `nome_normalizado`, GIST em `coordenada`) são **dropados antes do COPY** e **recriados depois** via `CREATE INDEX CONCURRENTLY` — carregar com esses índices ligados degrada muito o COPY. `CONCURRENTLY` **não pode** rodar dentro de transação, então a recriação acontece numa conexão em **autocommit** (sem `BeginTransaction`), na mesma conexão física do COPY. Um `DROP INDEX IF EXISTS` precede cada `CREATE` para limpar índices `INVALID` deixados por uma recriação anterior abortada.

No modo **`Recarga`** os índices permanecem (a tabela já está populada); só o merge `ON CONFLICT` roda. A política de *stale* (`vigente = false` para chaves ausentes na nova release) continua sendo da Story de atualização periódica (#674).

> O `logradouro_complemento` não é truncado no modo `Inicial` (só o `logradouro`, pela otimização de índices): seu merge `ON CONFLICT` já é idempotente e a remoção de registros que somem entre releases é tratada pela política de *stale* (#674), não por TRUNCATE.

## Recarga periódica (releases mensais)

A DNE é atualizada mensalmente. Para aplicar uma nova release (ex.: `202602`):

1. Restaurar o novo dump no banco `uniplus_geo_staging` (substitui o anterior).
2. Disparar a importação informando a versão `202602`.
3. O ETL faz **upsert por chave natural** — `sigla_iso`/`uf`/`codigo_ibge` são estáveis entre releases: insere o novo, atualiza o que mudou, grava `versao_dataset=202602` em cada linha tocada. Reaplicar a **mesma** versão é no-op (idempotente).

A política de *stale* (marcar `vigente=false` as chaves ausentes na nova release) e o gatilho operacional (seed em dev + endpoint admin `POST /api/admin/geo/importacoes`) são entregues na Story de atualização periódica do Epic.

## Parse tolerante (anti-frágil)

A fonte é toda `varchar` e traz `'-'`/vazio para dado ausente (≈27% dos municípios em `mortalidade_infantil`). Toda métrica externa é `nullable` e degrada para `null` sem abortar a carga — só chave natural e `nome` são obrigatórios.

Tipos do domínio por natureza da coluna (a fonte não os distingue):

- **Identificadores → `string`** (preservam zeros à esquerda, sem aritmética): `codigo_ibge`, `cep`/`cep_inicial`/`cep_final`, `ddd`, `uf`/siglas.
- **Métricas inteiras → `int?`**: `populacao_residente(_2022)`, `matriculas_ensino_fundamental_2023`, `rendimento_mensal_per_capita`, `total_veiculos_2023` (chegam inteiros na fonte real, ex.: `'1095'`, `'350273'`).
- **Métricas fracionárias / monetárias → `decimal?`**: `area_territorial_km2`, `densidade_demografica`, `idh`, `escolarizacao_6_a_14_anos`, `mortalidade_infantil`, `receitas`/`despesas` (≈bilhões), `pib_per_capita`. Sempre `InvariantCulture` (ponto decimal); vírgula é **rejeitada** (não vira separador de milhar).
- **`'S'`/`'N'` → `bool`**; **`aniversario` (`DD/MM`) → `string`** (não é data); **lat/long → `geography(Point,4326)`** com validação de domínio (`[-90,90]`/`[-180,180]`). Cada degradação é registrada (sem PII — métricas IBGE públicas) num **relatório de validação** com contadores por tabela (lidos/inseridos/atualizados/órfãos/duplicados/degradados) e amostras para auditoria. A carga só aborta por falha de infraestrutura (conexão/transação), com **rollback total** (transação única).
