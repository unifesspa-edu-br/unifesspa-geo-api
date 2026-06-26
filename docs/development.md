# Development

## Prerequisites

- .NET SDK 10.0.100 or compatible latest feature release.
- Docker for local PostgreSQL/PostGIS and adjacent infrastructure.

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
at `Unifesspa.UniPlus.*`.

## Local Commands

These commands are expected to become the standard gates after the namespace and
dependency extraction is complete:

```bash
dotnet restore Unifesspa.Geo.slnx --locked-mode
dotnet build Unifesspa.Geo.slnx
dotnet test Unifesspa.Geo.slnx --filter "Category!=Integration"
dotnet test Unifesspa.Geo.slnx --filter "Category=Integration"
dotnet format Unifesspa.Geo.slnx --verify-no-changes
bash tools/forbidden-deps/check.sh
bash tools/forbidden-deps/check-geo-independence.sh .
```

At this bootstrap stage, restore and build pass. Integration tests still require
the local Docker infrastructure described by the migrated compose files.

## OpenAPI

The canonical baseline is expected to live at `contracts/openapi.geo.json`.
Runtime drift must fail CI unless the baseline is intentionally regenerated and
reviewed.
