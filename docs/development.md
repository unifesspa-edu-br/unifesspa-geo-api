# Development

## Prerequisites

- .NET SDK 10.0.100 or compatible latest feature release.
- Docker for local PostgreSQL/PostGIS and adjacent infrastructure.

## Current Bootstrap State

This repository is in the extraction bootstrap branch.

The solution file is `Unifesspa.Geo.slnx`. It currently lists the extracted Geo
projects and Geo integration tests:

- `src/geo/Unifesspa.UniPlus.Geo.Domain`
- `src/geo/Unifesspa.UniPlus.Geo.Contracts`
- `src/geo/Unifesspa.UniPlus.Geo.Application`
- `src/geo/Unifesspa.UniPlus.Geo.Infrastructure`
- `src/geo/Unifesspa.UniPlus.Geo.API`
- `tests/Unifesspa.UniPlus.Geo.IntegrationTests`

The next bootstrap step is to remove dependencies on `Unifesspa.UniPlus.*` by
copying or adapting required code into Geo-owned namespaces.

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

At this bootstrap stage, restore/build are expected to fail until the remaining
`ProjectReference` entries to UniPlus shared projects and test fixtures are
replaced by Geo-owned code.

## OpenAPI

The canonical baseline is expected to live at `contracts/openapi.geo.json`.
Runtime drift must fail CI unless the baseline is intentionally regenerated and
reviewed.
