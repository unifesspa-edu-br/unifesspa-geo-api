# Development

## Prerequisites

- .NET SDK 10.0.100 or compatible latest feature release.
- Docker for local PostgreSQL/PostGIS and adjacent infrastructure.

## Local Infrastructure

Copy the compose environment template before starting dependencies:

```bash
cp docker/.env.example docker/.env
docker compose -f docker/docker-compose.yml --env-file docker/.env up -d postgres redis kafka apicurio keycloak
```

The compose stack includes the dependencies currently used by the Geo runtime:
PostGIS, Redis, Kafka/Apicurio and Keycloak. Test-only Keycloak data is
kept in `docker/keycloak/realm-e2e-tests.json`.

## Current Bootstrap State

This repository is in the extraction bootstrap branch.

The solution file is `Unifesspa.Geo.slnx`. It lists the extracted Geo projects,
Geo-owned foundation projects and Geo integration tests:

- `src/geo/Unifesspa.Geo.Domain`
- `src/geo/Unifesspa.Geo.Contracts`
- `src/geo/Unifesspa.Geo.Application`
- `src/geo/Unifesspa.Geo.Infrastructure`
- `src/geo/Unifesspa.Geo.API`
- `src/shared/Unifesspa.Geo.Kernel`
- `src/shared/Unifesspa.Geo.Governance.Contracts`
- `src/shared/Unifesspa.Geo.Application.Abstractions`
- `src/shared/Unifesspa.Geo.Infrastructure.Core`
- `tests/Unifesspa.Geo.IntegrationTests.Fixtures`
- `tests/Unifesspa.Geo.IntegrationTests`

The repository must remain free of dependencies on packages or projects rooted
at `Unifesspa.UniPlus.*`. Code copied during extraction is now owned by this
repository under `Unifesspa.Geo.*`.

Internal extension points copied from the Uni+ codebase were renamed to the Geo
foundation (`AddGeoOpenApi`, `AddGeoEncryption`, `AddGeoHealthChecks`,
`UseGeoNpgsqlConventions`) so future maintenance does not depend on UniPlus
package or namespace semantics.

## Authentication

The API uses JWT Bearer authentication backed by an OIDC authority:

- `Auth:Authority`: issuer/realm URL.
- `Auth:ValidateAudience`: optional strict `aud` validation. Default is
  `false` so any UNIFESSPA application token issued by the configured realm can
  consume Geo read endpoints.
- `Auth:Audience`: expected `aud` claim only when `Auth:ValidateAudience=true`.
- HTTPS metadata is required outside `Development`.
- JWT validation always checks issuer/realm, lifetime and signing key.
- 401 and 403 responses use `application/problem+json`.

Reference-data/query endpoints require an authenticated JWT from the configured
realm but do not require an administrative role. Operations that write, mutate
state or administer Geo, including endpoints under `/api/admin/geo`, require JWT
authentication and the `plataforma-admin` role.

The integration test suite includes a real Keycloak container and exercises the
production `JwtBearer` pipeline for valid tokens, tokens from another client in
the same realm, wrong issuer/realm, missing token and insufficient roles.

## Local Commands

Run the same gates used by CI before opening or updating a PR:

```bash
dotnet restore Unifesspa.Geo.slnx --locked-mode
dotnet build Unifesspa.Geo.slnx
dotnet test tests/Unifesspa.Geo.IntegrationTests/Unifesspa.Geo.IntegrationTests.csproj
dotnet format Unifesspa.Geo.slnx --verify-no-changes
bash tools/forbidden-deps/check.sh .
bash tools/forbidden-deps/check-geo-independence.sh .
```

Every test in this repository is an integration test, so there is no
unit-vs-integration split to filter on. CI runs the whole
`Unifesspa.Geo.IntegrationTests` project. To run a subset locally, filter by
test name, e.g.:

```bash
dotnet test tests/Unifesspa.Geo.IntegrationTests/Unifesspa.Geo.IntegrationTests.csproj \
  --filter "FullyQualifiedName~CepEndpointTests"
```

These tests require Docker because they use Testcontainers. The containers
actually started by the Geo suite are PostgreSQL/PostGIS and Keycloak.

## Commit Convention

Use Conventional Commits in pt-BR, following the UNIFESSPA organization pattern:

```text
feat(geo): adiciona endpoint de localidades
fix(geo): corrige validacao de audience jwt
test(geo): cobre autenticacao jwt real
docs(geo): documenta processo de release
ci(geo): torna scan trivy bloqueante
refactor(geo): remove nomes residuais do bootstrap
```

Prefer one semantic commit per concern: code behavior, tests, docs and CI should
not be mixed unless the change is intrinsically coupled.

## Container Image

The dedicated Geo Dockerfile is `docker/Dockerfile.geo`:

```bash
docker build -f docker/Dockerfile.geo -t unifesspa-geo-api:local .
```

## OpenAPI

The canonical baseline is expected to live at `contracts/openapi.geo.json`.
Runtime drift must fail CI unless the baseline is intentionally regenerated and
reviewed.
