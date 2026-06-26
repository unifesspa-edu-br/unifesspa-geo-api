# Release, rollback and consumption

## Release model

The Geo API publishes container images from this repository only.

Release tags use:

- Stable: `vX.Y.Z`
- Prerelease: `vX.Y.Z-rc.N`

Pushing a valid tag that points to a commit reachable from `main` runs
`.github/workflows/publish-image.yml` and publishes:

- `ghcr.io/unifesspa-edu-br/unifesspa-geo-api:<git-tag>`
- `ghcr.io/unifesspa-edu-br/unifesspa-geo-api:sha-<short-sha>`

The workflow performs a structural image smoke before pushing the multi-arch
image. It does not publish `latest`.

## Required pre-release gates

Before creating a release tag, run or verify the same gates used by CI:

```bash
dotnet restore Unifesspa.Geo.slnx --locked-mode
dotnet build Unifesspa.Geo.slnx
dotnet test tests/Unifesspa.Geo.IntegrationTests/Unifesspa.Geo.IntegrationTests.csproj
dotnet format Unifesspa.Geo.slnx --verify-no-changes
bash tools/forbidden-deps/check.sh .
bash tools/forbidden-deps/check-geo-independence.sh .
npx --yes @stoplight/spectral-cli@6.15.0 lint --ruleset tools/spectral/.spectral.yaml --fail-severity=error contracts/openapi.geo.json
docker build -f docker/Dockerfile.geo -t unifesspa-geo-api:local .
```

## OpenAPI contract

The canonical committed contract is:

- `contracts/openapi.geo.json`

The runtime endpoint remains:

- `GET /openapi/geo.json`

Contract drift is intentional only when this command changes the committed
baseline and the diff is reviewed:

```bash
UPDATE_OPENAPI_BASELINE=1 dotnet test tests/Unifesspa.Geo.IntegrationTests/Unifesspa.Geo.IntegrationTests.csproj --filter SpecRuntime
```

Consumers must use the dedicated repository baseline, a release artifact, or the
runtime OpenAPI endpoint. They must not depend on a copy of
`contracts/openapi.geo.json` inside `uniplus-api`.

## Consumer integration rules

Uni+ and other UNIFESSPA services consume Geo through HTTP/OpenAPI contracts.

Consumers must not:

- Reference `Unifesspa.Geo.*` projects directly.
- Reference old `Unifesspa.UniPlus.Geo.*` projects directly.
- Add foreign keys to Geo database tables.
- Require backend runtime calls to validate already persisted city/address
  snapshots that are designed to be snapshot-based.

Consumers may:

- Resolve city, address, CEP, district, neighborhood, street and proximity data
  through the published Geo API.
- Persist approved snapshots or references derived from Geo responses.
- Regenerate clients from the published OpenAPI contract.

## Cutover rule

During environment promotion, only one Geo runtime may own database migrations,
ETL workers, seed and reconciliation for a given environment.

Before starting a dedicated Geo image in an environment that still runs the old
Geo deployable from `uniplus-api`, stop the old runtime or disable its migration
and ETL ownership.

## Rollback

Rollback target:

- Prefer the previous known-good `ghcr.io/unifesspa-edu-br/unifesspa-geo-api:<tag>`.
- During the extraction transition only, operators may roll back to the last
  known-good Geo image produced from `uniplus-api` if the dedicated image has
  not yet been promoted successfully.

Rollback steps:

1. Stop the failing dedicated Geo runtime.
2. Confirm no second Geo runtime is running migrations, ETL, seed or
   reconciliation for the same database.
3. Redeploy the selected known-good image.
4. Verify `/health/live`, `/health/ready` and `GET /openapi/geo.json`.
5. Run smoke checks for CEP lookup, estados/cidades, hierarquia and proximidade.
6. Record the image tag, commit SHA, database state and smoke result in the
   operational change record.
