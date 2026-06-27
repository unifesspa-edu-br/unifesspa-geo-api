# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## O que é este repositório

`unifesspa-geo-api` é o **bounded context Geo** do Uni+ — API institucional transversal de
**localidades, endereçamento e georreferência** da UNIFESSPA (país, UF, município, distrito,
bairro, logradouro, CEP, coordenadas). É *reference data* de origem externa (IBGE + DNE dos
Correios): leitura intensa, escrita rara via ETL.

Foi **extraído do monólito modular `uniplus-api`** para um repositório dedicado que governa
código, contrato OpenAPI, testes, imagem e release de forma independente (ADR-0090). O código
foundation copiado durante a extração foi renomeado para `Unifesspa.Geo.*` e **agora é próprio
deste repo**.

> O `CLAUDE.md` e o `docs/visao-do-projeto.md` da raiz do workspace (`uniplus/`) **também se
> aplicam** — convenções de commit, idioma (pt-BR), workflow issue-driven, sem `Co-Authored-By`/
> atribuição de IA. Não duplicar aqui; este arquivo cobre só o que é específico do Geo.

### Invariante de independência (gate de CI bloqueante)

Este repo **não pode** depender de nada enraizado em `Unifesspa.UniPlus.*` — nem
`ProjectReference`, nem `PackageReference`, nem `using`, nem `namespace`, nem entrada em
`packages.lock.json`. O gate `tools/forbidden-deps/check-geo-independence.sh` falha o CI se
qualquer um reaparecer. Ao copiar/adaptar código do Uni+, renomeie tudo para `Unifesspa.Geo.*`.

## Comandos

Pré-requisito: .NET SDK **10.0.100** (fixado em `global.json`, `rollForward: latestFeature`).
Solution: **`Unifesspa.Geo.slnx`** (formato `.slnx`, não `.sln`).

```bash
# Gates de CI — rodar todos antes de abrir/atualizar PR (ver docs/development.md)
dotnet restore Unifesspa.Geo.slnx --locked-mode          # restore travado no lockfile
dotnet build   Unifesspa.Geo.slnx --configuration Release
dotnet test    tests/Unifesspa.Geo.IntegrationTests/Unifesspa.Geo.IntegrationTests.csproj
dotnet format  Unifesspa.Geo.slnx --verify-no-changes    # estilo é gate, não opcional
bash tools/forbidden-deps/check.sh .                      # pacotes vetados (FluentAssertions)
bash tools/forbidden-deps/check-geo-independence.sh .     # zero deps Unifesspa.UniPlus.*
```

```bash
# Rodar UM teste (todos os testes aqui são de integração e exigem Docker)
dotnet test tests/Unifesspa.Geo.IntegrationTests/Unifesspa.Geo.IntegrationTests.csproj \
  --filter "FullyQualifiedName~CepEndpointTests"

# Subir a infra local (PostGIS, Redis, Kafka/Apicurio, Keycloak)
cp docker/.env.example docker/.env
docker compose -f docker/docker-compose.yml --env-file docker/.env up -d \
  postgres redis kafka apicurio keycloak

# Imagem do container (Dockerfile dedicado, não na raiz)
docker build -f docker/Dockerfile.geo -t unifesspa-geo-api:local .
```

Os **testes de integração exigem Docker** — os Testcontainers efetivamente usados sobem apenas
**PostgreSQL/PostGIS** e **Keycloak**. Não há projeto de testes unitários separado: a solution lista
só `IntegrationTests` + `IntegrationTests.Fixtures`. Testes usam `[Fact(DisplayName = "CA-NN: …")]`
e compartilham containers via `[Collection(GeoPostgisCollection.Name)]` (a grande maioria) e a
collection do `KeycloakContainerFixture`. (As fixtures `Vault`/`OtelCollector` existem na
pasta de fixtures como herança da extração, mas nenhum teste do Geo as usa.)

## Arquitetura

Clean Architecture com **CQRS sobre Wolverine**. Nove projetos: cinco do contexto Geo + quatro de
fundação compartilhada (própria do repo).

```
src/geo/                              src/shared/  (fundação, owned pelo repo)
  Unifesspa.Geo.Domain        ←─        Unifesspa.Geo.Kernel               (Result, paginação, base de domínio)
  Unifesspa.Geo.Contracts               Unifesspa.Geo.Application.Abstractions (IQueryBus/ICommandBus, IQuery/ICommand, auth)
  Unifesspa.Geo.Application             Unifesspa.Geo.Governance.Contracts
  Unifesspa.Geo.Infrastructure          Unifesspa.Geo.Infrastructure.Core  (OpenAPI, HATEOAS, errors, cripto,
  Unifesspa.Geo.API  (host)                                                 messaging Wolverine, health, pagination…)
```

### Fluxo de uma leitura (padrão dominante)

`Controller` (`src/geo/…/API/Controllers/`) → `IQueryBus.Send(query)` →
`…Application/Queries/<área>/<Nome>QueryHandler` → reader EF Core
(`…Infrastructure/Persistence/Readers/`) → `Result` mapeado para `DTO` →
controller anexa **HATEOAS** (`IResourceLinksBuilder<T>`) e devolve.

- **CQRS via Wolverine**: queries/commands são despachados por `IQueryBus`/`ICommandBus`
  (`src/shared/…Application.Abstractions/Messaging`). A implementação (`WolverineQueryBus`)
  fica em `Infrastructure.Core/Messaging`. **Nunca** importar `Wolverine.IMessageBus` fora de
  `Infrastructure.Core/Messaging/` — a separação Query/Command é semântica e enforced por tipo
  (ADR-0003). Apesar do `visao-do-projeto.md` citar MediatR, **aqui o backbone é Wolverine 5.x**.
- **Persistência**: EF Core 10 + Npgsql sobre PostgreSQL/**PostGIS**; tipos `NetTopologySuite`
  (`Point`) para georreferência (ADR-0091). `GeoDbContext` em `…Infrastructure/Persistence`.
- **Paginação por cursor opaco** (keyset via biblioteca MR sob cursor base64; ADR-0094/0095):
  `[FromCursor(tag, RequireSortKey = true)]` no parâmetro `PageRequest`; a chave de ordenação é
  **sempre não-nula** (ADR-0095). Resposta via `OkPaginatedOrdenadoAsync` + header `Link`.
- **Contrato HTTP**: respostas de erro em `application/problem+json`; vendor media types via
  `[VendorMediaType(Resource="…", Versions=[1])]`; OpenAPI 3.1 servido em `/openapi/geo.json`.
- **ETL DNE** (`…Infrastructure/Persistence/Etl`, ADR-0092): worker hospedado que carrega o
  dataset DNE/IBGE (dumps proprietários, **não versionados**) via schema de staging + SELECT
  streamado. O reference data é **isento de soft-delete**. Detalhes em `docs/geo-etl-dataset-dne.md`.
- **Autenticação**: JWT Bearer/OIDC (Keycloak). Endpoints de leitura exigem JWT **do realm
  configurado** (sem role admin); escrita/admin (`/api/admin/geo`) exige a role `plataforma-admin`.
  `Auth:ValidateAudience` default `false`. Ver seção *Authentication* em `docs/development.md`.

### Ordem de boot crítica (`API/Program.cs`)

`AddDbContextMigrationsOnStartup<GeoDbContext>` e os gatilhos de ETL são registrados **antes** de
`UseWolverineOutboxCascading` + `AddWolverineMessaging` (invariante #419, coberto por fitness
test). Migrations e seed tocam o banco e precisam acontecer antes do runtime de mensageria subir.

## Convenções e armadilhas específicas

- **`TreatWarningsAsErrors=true`** em todo o repo (`Directory.Build.props`) + `AnalysisLevel=latest-all`.
  Nunca silenciar warning inline; supressões justificadas vão em `GlobalSuppressions.cs`.
- **Controllers devem ser `public`** (o `ControllerFeatureProvider` só descobre tipos públicos) —
  daí o `CA1515` suprimido por classe com justificativa.
- O analyzer transitivo `MR.EntityFrameworkCore.KeysetPagination.Analyzers` é **removido em build**
  por um target em `Directory.Build.props` (emite `CS9057` que viraria erro no publish Docker). Não
  reverter; a regra de chave não-nula é garantida por design + testes, não pelo analyzer.
- **`FluentAssertions` é proibido** (comercial a partir da v8) → usar `AwesomeAssertions`
  (gate `forbidden-deps`, ADR-0021).
- Versões de pacote centralizadas em `Directory.Packages.props` (CPM); lockfiles (`packages.lock.json`)
  são commitados — `restore --locked-mode` falha se ficarem dessincronizados.
- **Drift de OpenAPI é gate**: o runtime gera o spec e o CI compara com o baseline
  `contracts/openapi.geo.json` + lint Spectral (`tools/spectral/.spectral.yaml`). Ao mudar contrato,
  regenerar o baseline conscientemente. CodeQL e Trivy (bloqueante para `HIGH`/`CRITICAL`) também rodam.

## Commits

Conventional Commits em pt-BR, indicativo presente 3ª pessoa, escopo **`geo`**, atômicos
(um concern por commit — código, testes, docs e CI não se misturam salvo acoplamento intrínseco):

```
feat(geo): adiciona endpoint de localidades
fix(geo): corrige validacao de audience jwt
test(geo): cobre autenticacao jwt real
ci(geo): torna scan trivy bloqueante
```

Sem `Co-Authored-By` e sem qualquer atribuição de IA em commits ou PRs (regra do workspace).

## ADRs

Decisões do módulo em `docs/adrs/` (0090–0096): contexto dedicado (0090), PostGIS/NTS (0091),
ETL DNE (0092), rate limiting na borda (0093), keyset via MR sob cursor opaco (0094), chave de
ordenação não-nula (0095), endereço como referência estruturada (0096).

ADRs com numeração `00xx` fora dessa faixa (ex.: 0029, 0054, 0063) pertencem ao acervo do
`uniplus-api`/`uniplus-docs` e **não existem neste repo**. Referencie-os apenas como menção
textual (`ADR-0054`) — **nunca** como hyperlink. O repositório não mantém links de ADR
cross-repository (nem markdown relativo, nem `<see href>`, nem URL absoluta para outro repo),
coerente com a regra de independência do topo deste arquivo.
