# Carga da base dos Correios (DNE) no Geo

Guia de operação da atualização dos dados de localidades, endereçamento e georreferência do Geo a partir do pacote dos Correios (DNE) e da lista de municípios do IBGE. Decisões em [ADR-0090](adrs/0090-modulo-geo-localidades.md), [ADR-0091](adrs/0091-postgis-georreferencia-nts.md), [ADR-0092](adrs/0092-etl-carga-dne-reference-data.md), [ADR-0097](adrs/0097-nome-do-municipio-pelo-ibge.md) e [ADR-0098](adrs/0098-carga-da-base-dos-correios-pelo-envio-do-pacote.md).

## Visão geral

A atualização é feita enviando o pacote dos Correios pela própria API. Não há banco de preparação nem restauração manual: o Geo recebe o arquivo, confere o conteúdo, carrega os dados num esquema temporário do seu banco e publica tudo de uma só vez. Se algo falhar, a versão anterior continua publicada.

O pacote dos Correios é proprietário (exige licença institucional) e **não é versionado** neste repositório.

## Como obter o pacote

O pacote é o arquivo zip distribuído pelos Correios, por exemplo `tbl_cep_AAAAMM_postgresql.zip`. Ele contém:

- 15 arquivos `tbl_cep_AAAAMM_n_{tabela}.sql`, um por tabela, todos no topo do zip ou todos dentro de uma única pasta (por exemplo `postgresql/`), com ou sem a entrada da própria pasta;
- opcionalmente, arquivos que acompanham o pacote, como a imagem `0relacionamentos.jpg` ou `0relacionamentos.png`, que o Geo ignora sem abrir.

A versão do pacote é lida do nome dos 15 arquivos `.sql`, e todos devem ter a mesma versão. O nome do zip não conta: um zip pode ter nome de um mês e conter tabelas do mês seguinte.

## Como enviar

O envio exige um token de acesso com o papel `plataforma-admin`. O arquivo vai direto no corpo da requisição, sem formulário:

```bash
curl -i -X POST http://localhost:5270/api/admin/geo/importacoes \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/zip" \
  --data-binary @pacote.zip
```

`$TOKEN` é o token do operador. O tamanho máximo do pacote é 128 MiB por padrão (`Geo:Etl:TamanhoMaximoDoPacote`).

### Respostas

| Código | Significado | O que fazer |
|---|---|---|
| 202 | Pacote aceito; a carga começou em segundo plano. O cabeçalho `Location` aponta para o acompanhamento da execução. | Acompanhar pelo `Location`. |
| 200 | Esse mesmo pacote já é a publicação vigente (mesmo resumo SHA-256 da última carga concluída). Nada foi repetido; o corpo traz essa execução. | Nada. |
| 401 | Sem token válido. | Obter um token. |
| 403 | O token não tem o papel `plataforma-admin`. | Usar uma conta com o papel. |
| 409 | Há uma carga sendo executada (`em_andamento`), ou a versão do pacote é anterior à última aplicada (`versao_nao_progressiva`). | Aguardar o fim da carga, ou enviar uma versão igual ou mais nova. |
| 413 | O pacote passa do tamanho máximo (`pacote_acima_do_limite`). | Conferir o arquivo e o limite configurado. |
| 415 | O tipo de conteúdo não é `application/zip` (`tipo_de_conteudo_nao_suportado`). | Enviar com `Content-Type: application/zip`. |
| 422 | O conteúdo do zip não confere (`pacote_invalido`): não é um zip, falta ou sobra tabela, as entradas estão em pastas diferentes, há arquivo cifrado, o zip tem mais entradas que o limite, tamanho declarado acima do limite ou versões diferentes entre as tabelas. Todas as faltas vêm juntas na resposta. | Corrigir o pacote e reenviar. |
| 503 | A carga foi registrada, mas não pôde entrar na fila porque o serviço está desligando (`nao_enfileirada`). | Reenviar depois que o serviço voltar. |

Os erros usam `application/problem+json`. A autorização (401 e 403) é decidida antes de ler o corpo; o tipo de conteúdo e o tamanho declarado, também. O limite de tamanho vale ainda durante o recebimento, para envios sem tamanho declarado.

### Conferência feita na hora do envio

Antes de responder 202, o Geo confere a lista de arquivos do zip, sem descompactar:

- exatamente as 15 tabelas, cada uma uma vez; um arquivo `.sql` que não é uma das 15 é recusado;
- todas as entradas no topo ou todas na mesma pasta, de um só nível, com nome de até 64 caracteres entre letras, números, `_` e `-`; a entrada da própria pasta é opcional e deve estar vazia; nomes com `\`, `..`, `:` ou mais de uma `/` são recusados;
- os demais arquivos (imagem, por exemplo) são aceitos e ignorados, cada um com tamanho declarado de até 5 MiB;
- no máximo `QuantidadeMaximaDeEntradasDoPacote` entradas (padrão 32, contando a da pasta), nomes de até 128 caracteres, nenhuma entrada cifrada;
- tamanhos declarados dentro dos limites (768 MiB por arquivo e 1 GiB no total);
- a mesma versão AAAAMM válida em todas as tabelas.

Na leitura de cada arquivo `.sql`, o `INSERT` pode cruzar linhas só dentro de texto entre aspas simples (até 8 linhas e 64 KiB por comando); o texto guarda as quebras de linha como estão no arquivo. Fora das aspas, cada comando continua numa só linha.

## Acompanhar a execução

Use o endereço do cabeçalho `Location` (`GET /api/admin/geo/importacoes/{id}`), com o mesmo token:

```bash
curl -s http://localhost:5270/api/admin/geo/importacoes/{id} \
  -H "Authorization: Bearer $TOKEN"
```

A situação (`status`) é `EmAndamento`, `Concluida` ou `Falhou`. A resposta traz a versão, o resumo SHA-256 e o tamanho do pacote, a mensagem (em caso de falha) e, ao concluir, o relatório com os contadores por tabela (lidos, inseridos, atualizados, órfãos, duplicados e degradados). Com o conjunto completo, a carga leva alguns minutos e é dominada pela cópia dos cerca de 1,4 milhão de logradouros.

Problemas no conteúdo dos arquivos `.sql` (comando desconhecido, coluna faltando, linha malformada) são descobertos durante a carga, depois do 202. Nesse caso a execução termina como `Falhou`, e a mensagem indica a tabela e o número da linha, nunca o conteúdo do arquivo.

## Repetição e versões

- Reenviar o pacote cujo resumo SHA-256 é o da última carga concluída devolve 200 e não repete nada. Não há opção de forçar a recarga do mesmo pacote.
- Enviar um pacote diferente da mesma versão (uma correção) inicia uma nova carga.
- Versão anterior à última aplicada é recusada com 409. Cargas que falharam não contam para esse limite.
- Uma carga por vez: um segundo envio durante a carga recebe 409 antes de o corpo ser lido.

## Falha mantém a versão anterior

A publicação é uma única transação. Falha, cancelamento, estouro de `Geo:Etl:DuracaoMaximaDaCarga` (1 hora por padrão) ou queda do serviço antes da confirmação desfazem tudo: a base publicada continua a versão anterior e a execução termina como `Falhou`. Quem consulta a API vê a versão anterior até o instante da confirmação.

## O que é descartado, e quando

- **O arquivo do pacote** fica num volume em memória do pod (`Geo:Etl:DiretorioDoPacote`), com permissão `0600`, sem cifra no aplicativo. É apagado logo que a carga termina de popular o esquema temporário, em qualquer recusa (409, 413, 422, 200 por repetição), em qualquer falha ou cancelamento e na partida do serviço.
- **O esquema temporário** `carga_dne_{execução}`, no banco do Geo, é descartado ao fim da carga, em sucesso ou falha, e também na partida do serviço, quando a instância obtém a trava.
- **A trava da carga** (trava consultiva do PostgreSQL, uma por vez) é obtida no pedido, depois da conferência do pacote, acompanha a execução e é solta ao fim.
- Depois de um reinício do serviço, o pacote precisa ser reenviado. O serviço só marca como `Falhou` as execuções em andamento quando consegue obter a trava da carga, isto é, quando nenhuma instância está carregando, e faz isso na partida e também no envio seguinte. Se outra instância segura a trava, a execução dela está viva e nada é alterado.
- Evite implantar uma nova versão durante uma carga: o serviço antigo cancela a carga e desfaz a transação.

## Opções de configuração (`Geo:Etl`)

| Opção | Padrão | Função |
|---|---|---|
| `TamanhoMaximoDoPacote` | 128 MiB | Tamanho máximo do pacote enviado. |
| `QuantidadeMaximaDeEntradasDoPacote` | 32 | Quantidade máxima de entradas do zip, contando a da pasta; aceita de 16 a 256, fora disso a aplicação não inicia. |
| `DiretorioDoPacote` | `pacotes-dne` no diretório temporário do sistema | Onde o arquivo fica enquanto a carga não popula o esquema temporário (em implantação, o volume em memória do pod). |
| `DuracaoMaximaDaCarga` | 1 hora | Prazo da carga; passado o prazo, ela é cancelada e desfeita. Aceita mais que zero e até 49 dias (o limite do cancelamento da carga é de cerca de 49,7 dias); fora disso a aplicação não inicia. |
| `TempoLimiteDosComandosDaCarga` | 30 minutos | Prazo de cada comando longo da carga. Aceita mais que zero e até 2147483 segundos (cerca de 24,8 dias, limite do driver do banco), fora disso a aplicação não inicia. |
| `IntervaloDoSinalDeVidaDaTrava` | 30 segundos | Intervalo do sinal de vida da conexão que segura a trava da carga; aceita de 1 segundo a 5 minutos, fora disso a aplicação não inicia. |
| `ProcessoEmSegundoPlanoHabilitado` | `true` | Liga o serviço que executa as cargas; desligado, o envio é aceito e nada o consome. Só desligar em teste. |

## Carga em desenvolvimento

Com a infraestrutura local no ar (`docs/development.md`) e a API em execução (`dotnet run --project src/geo/Unifesspa.Geo.API`), envie um pacote real, se você tiver licença e autorização, ou um pacote sintético de teste, pelo comando da seção "Como enviar". Depois de uma carga concluída, confirme que um CEP do pacote resolve:

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5270/api/cep/{cep-do-pacote}
```

A resposta esperada é 200. Nada é carregado automaticamente na partida da API.

## Como a carga funciona por dentro

1. O pacote é recebido em fluxo e gravado no volume em memória, com o resumo SHA-256 calculado durante a gravação.
2. O Geo lê cada arquivo `.sql` por uma gramática fechada: reconhece só os comandos conhecidos do pacote, confere colunas e tipos de cada tabela contra uma lista fixa e **nunca executa SQL do arquivo**. Os valores são tipados e enviados ao banco por cópia binária.
3. Os dados são carregados em tabelas `UNLOGGED` (que não geram registro de transações) do esquema `carga_dne_{execução}`.
4. Numa única transação, o Geo publica país, estado e cidade, depois distrito, bairro, grande usuário, CEP geral, complementos e logradouros, marca os registros que não estão mais na versão e conclui a execução.

## Carga atômica e em lote das folhas — Distrito, Bairro e Logradouro

Toda a publicação de uma versão — topo (`pais`, `estado`, `cidade`), folhas (`distrito`/`bairro` + faixas, `cep_grande_usuario`, `cep_geral`, `logradouro_complemento` e os ~1,4M `logradouro`), marcação de registros não vigentes e conclusão da execução — executa sob trava consultiva no PostgreSQL e em **uma única transação** sobre o `GeoDbContext`. Se ocorrer qualquer falha, cancelamento ou estouro de `Geo:Etl:DuracaoMaximaDaCarga` (padrão de 1 hora), a transação é revertida por inteiro e a versão anterior publicada permanece intacta.

### Trava da carga e reinício do serviço

- **Ordem do pedido:** o endpoint recebe e confere o pacote e só então obtém a trava consultiva do PostgreSQL (`pg_try_advisory_lock`, em conexão própria), antes da guarda de versão e do registro da execução como `EmAndamento`. A trava não cobre o recebimento do pacote. Antes de ler o corpo, o envio é recusado com 409 `em_andamento` só se há execução em andamento e a trava está ocupada; com a trava livre, a execução em andamento está abandonada e é encerrada como `Falhou`, depois de descartar os esquemas temporários que sobraram, e o envio segue. Se o descarte falhar, a execução segue `EmAndamento` e o envio recebe o erro do servidor, sem 409. Depois de receber o pacote, com a trava ocupada, responde 409 `em_andamento` sem gravar nada. Recusa posterior (versão anterior à aplicada, conflito de registro, fila fechada) solta a trava.
- **Duração:** a trava acompanha a execução pela fila em memória e só é solta quando a carga termina, depois de marcar o resultado (`Concluida` ou `Falhou`). No desligamento, as cargas ainda na fila são marcadas `Falhou` antes de a trava ser solta. A conclusão só é gravada se a execução ainda está `EmAndamento`, na mesma transação da publicação: se a trava foi solta pelo banco com a carga viva e a execução já foi encerrada como `Falhou`, a conclusão é recusada, a publicação é desfeita (a base anterior continua) e o pacote precisa ser reenviado.
- **Reinício e abandono:** na partida, e também no envio seguinte, o serviço tenta obter a mesma trava. Se conseguir, toda execução `EmAndamento` é tratada como abandonada (a execução viva tem a trava; se o banco a soltou com a carga viva, a conclusão dela é recusada, como descrito acima), descarta os esquemas temporários que sobraram e marca todas como `Falhou`. Só a partida, na mesma seção sob a trava, sincroniza também o selo do cache de CEP com a última execução `Concluida`; o envio apenas encerra como `Falhou` e descarta os esquemas. Se a trava estiver ocupada, outra instância está carregando (por exemplo, durante a implantação com duas instâncias): nada é alterado e o selo fica a cargo de quem detém a trava, que o grava ao confirmar a carga.
- **Sinal de vida da conexão:** a conexão que segura a trava fica parada durante toda a carga, e uma rede ou um banco que derrube conexão ociosa soltaria a trava com a carga ainda em execução. Por isso essa conexão, e só ela, manda sinal de vida ao banco pelo protocolo do PostgreSQL (parâmetro `Keepalive` do Npgsql) a cada `Geo:Etl:IntervaloDoSinalDeVidaDaTrava` (30 segundos por padrão). Fração de segundo é arredondada para cima. O valor de homologação e produção fica no chart de implantação (`uniplus-infra`), não neste repositório. O sinal só protege enquanto a conexão está parada: não atua durante um comando em execução.

O pipeline das folhas (`GeoImportadorLocalidades`) organiza a carga em duas etapas dentro dessa mesma transação:

1. **Upsert (`GeoImportadorDistritoBairro`)** — `distrito`/`bairro` (+faixas), `cep_grande_usuario` e `cep_geral`, por chave natural. O `cep_geral` guarda o CEP oficial de cidade ou distrito, que a DNE entrega como linha de `logradouro` sem nome (tipo e nomes vazios); essas linhas saem do COPY de logradouros e não contam como ignoradas. Chave natural: `cep`. Esta etapa também **resolve as FKs**: monta os dicionários `id_cidade → Cidade.Id (Guid)` (via JOIN do esquema de carga com o domínio pelo código IBGE) e `id_distrito`/`id_bairro → Guid` (os ids da DNE são as PKs da fonte, únicos dentro de uma versão; homônimos em cidades distintas viram entidades distintas).
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

- **COPY binário** (`NpgsqlBinaryImporter`) em lotes sobre a transação ativa do `GeoDbContext`, com leitura **em streaming** do esquema de carga — uso de memória estável mesmo nos 1,4M logradouros. A coordenada é escrita explicitamente como `NpgsqlDbType.Geography` (`geography(Point,4326)`).
- **Dedup intra-fonte** via `DISTINCT ON (chave natural) ... ORDER BY ..., ordem_de_copia DESC` (a última linha vence; `ordem_de_copia` é um `bigserial` preenchido na ordem do COPY) — evita o erro do Postgres "ON CONFLICT cannot affect row a second time".
- **Idempotência** via `ON CONFLICT (chave natural) DO UPDATE`: preserva `id` (Guid v7) e `created_at`, carimba `updated_at` e reativa `vigente = true`. A chave de `logradouro` é `(cep, nome_normalizado, cidade_id)`; a de `logradouro_complemento`, `(cep, complemento_normalizado)`.
- **Auditoria sem interceptor**: como o COPY e a mesclagem ignoram o `AuditableInterceptor`, o `created_at` é carimbado pelo `TimeProvider` (no insert) e o `updated_at` só no `DO UPDATE` — tudo num único instante por carga.
- **Índices preservados na transação única**: os índices de `logradouro` (`gin_trgm` em `nome_normalizado` e GIST em `coordenada`) permanecem ativos durante toda a carga; não há modo inicial separado com `TRUNCATE` nem `CREATE INDEX CONCURRENTLY`.

## Recarga periódica (versões mensais)

A DNE é atualizada mensalmente. Para aplicar uma nova versão (ex.: `202602`):

1. Obter o pacote novo dos Correios.
2. Enviá-lo pelo endpoint administrativo (`POST /api/admin/geo/importacoes`, `application/zip`), como descrito acima. A versão `202602` sai do conteúdo do pacote.
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

Os nomes só mudam quando uma carga roda. Ao atualizar a imagem do Geo num ambiente que já tem cidades (HML, produção, base local), a API continua devolvendo os nomes anteriores até a próxima carga concluída, e a busca pela grafia oficial não encontra o município. Reenviar o mesmo pacote já publicado devolve 200 sem repetir a carga; os nomes novos entram com o envio de um pacote diferente (por exemplo, a próxima versão mensal). Depois dela, confira, por exemplo: `GET /api/cidades/1505551` deve devolver «Pau D'Arco». A carga mantém o identificador de cada município.

## Parse tolerante (anti-frágil)

A fonte é toda `varchar` e traz `'-'`/vazio para dado ausente (≈27% dos municípios em `mortalidade_infantil`). Toda métrica externa é `nullable` e degrada para `null` sem abortar a carga — só chave natural e `nome` são obrigatórios.

Tipos do domínio por natureza da coluna (a fonte não os distingue):

- **Identificadores → `string`** (preservam zeros à esquerda, sem aritmética): `codigo_ibge`, `cep`/`cep_inicial`/`cep_final`, `ddd`, `uf`/siglas.
- **Métricas inteiras → `int?`**: `populacao_residente(_2022)`, `matriculas_ensino_fundamental_2023`, `rendimento_mensal_per_capita`, `total_veiculos_2023` (chegam inteiros na fonte real, ex.: `'1095'`, `'350273'`).
- **Métricas fracionárias / monetárias → `decimal?`**: `area_territorial_km2`, `densidade_demografica`, `idh`, `escolarizacao_6_a_14_anos`, `mortalidade_infantil`, `receitas`/`despesas` (≈bilhões), `pib_per_capita`. Sempre `InvariantCulture` (ponto decimal); vírgula é **rejeitada** (não vira separador de milhar).
- **`'S'`/`'N'` → `bool`**; **`aniversario` (`DD/MM`) → `string`** (não é data); **lat/long → `geography(Point,4326)`** com validação de domínio (`[-90,90]`/`[-180,180]`) feita em `PosicaoGeografica`: se latitude ou longitude falta, não é número ou está fora do domínio (inclusive valor que não cabe na coluna `numeric(9,6)`), latitude, longitude e ponto ficam todos vazios, em qualquer tabela com coordenada. Cada degradação é registrada (sem PII — métricas IBGE públicas) num **relatório de validação** com contadores por tabela (lidos/inseridos/atualizados/órfãos/duplicados/degradados) e amostras para auditoria. A carga só aborta por falha de infraestrutura (conexão/transação), com **rollback total** (transação única).
