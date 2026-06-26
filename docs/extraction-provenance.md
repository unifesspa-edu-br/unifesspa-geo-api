# Geo extraction provenance

This repository was bootstrapped from `unifesspa-edu-br/uniplus-api` as part of
OpenSpec change `move-geo-api-to-dedicated-repository`.

## Filtered history

The branch `bootstrap/732-extract-geo-history` was created from
`uniplus-api` `origin/main` at commit
`1526851da50bd76c0713f3c1046d2ab0e9954f10`.

History was filtered to preserve commits touching these paths:

- `src/geo/**`
- `tests/Unifesspa.UniPlus.Geo.IntegrationTests/**`
- `contracts/openapi.geo.json`
- `docs/geo-etl-dataset-dne.md`
- `docs/adrs/0090-*` through `docs/adrs/0096-*`

The filtered history produced 71 commits and 241 tracked files before the
bootstrap infrastructure commit.

## Directly copied bootstrap files

The following files were copied directly from `uniplus-api` because they are
shared repository infrastructure and were not part of the path-filtered history:

- `.editorconfig`
- `.gitignore`
- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`
- `docker/docker-compose.yml`
- `docker/init-db.sql`
- `tools/forbidden-deps/check.sh`
- `tools/forbidden-deps/README.md`
- `tools/spectral/.spectral.yaml`

The independence gate was copied from the OpenSpec change artifacts:

- `tools/forbidden-deps/check-geo-independence.sh`

## Ownership rule

Copied code and infrastructure are owned by this repository after extraction.
Future changes should not synchronize automatically from `uniplus-api`.

The dedicated Geo repository must not depend on `ProjectReference` or
`PackageReference` entries rooted at `Unifesspa.UniPlus.*`; required behavior
must be copied or adapted under the Geo namespace.

The bootstrap branch currently uses `Unifesspa.Geo.*` project and namespace
roots for production and test code.
