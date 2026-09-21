# epr-packaging-data-archive

A read API for Extended Producer Responsibility packaging data, giving one place to ask what an
organisation has reported, whether that organisation is a direct producer or a member of a compliance
scheme.

> [!IMPORTANT]
> **The purpose of this service is being reconfirmed.** The code here reads as a query API over
> packaging data, while the repository is named `...-archive`, which implies a different job. Until
> that is settled, treat the endpoints below as an inherited shape rather than a decided contract.

The service runs on [CDP](https://portal.cdp-int.defra.cloud) as a protected-zone backend. It sits in
front of the Azure `epr-common-data-api` and consumes it; it does not replace it.

* [Status](#status)
* [Quick start](#quick-start)
* [Endpoints](#endpoints)
* [Response shape](#response-shape)
* [Where the data comes from](#where-the-data-comes-from)
* [Testing](#testing)
* [Running locally](#running-locally)
* [NuGet sources](#nuget-sources)
* [Dependabot](#dependabot)

## Status

**Phase one of three. The API serves fixtures, not real data.**

| Phase | State | What it does |
|---|---|---|
| 1. Contract and stubs | **Done** | Eight `GET` endpoints serving in-memory fixtures. No database, no outbound calls. |
| 2. Common Data API | Not started | An adapter reading the Azure warehouse through the CDP egress proxy. |
| 3. Persistence | Not started | A locally held projection, once a store is chosen. |

Every response says which it is: check `meta.source`, which currently reads `"stub"`.

The point of phase one is that the contract is real even though the data is not, so a consuming team
can generate a client and start building against it now. The OpenAPI document at
`/openapi/v1.json` is the machine-readable version of everything below.

Two decisions are still open, both tracked outside this repo:

- **The persistent store.** MongoDB and Aurora PostgreSQL are both available to a protected-zone
  backend on CDP. Postgres is a restricted Beta needing platform team approval. Nothing in phase one
  depends on the answer.
- **Provisioning.** This service has no tenant definition in `cdp-tenant-config` and no configuration
  in `cdp-app-config` yet, so it cannot be deployed until PRs land in both.

## Quick start

```bash
dotnet restore --source https://api.nuget.org/v3/index.json   # see NuGet sources below
dotnet build
dotnet test                                                   # 121 tests, about 1 second
dotnet run --project EprPackagingDataArchive
```

Then:

```bash
curl http://localhost:8085/health
curl http://localhost:8085/organisations/100123
curl "http://localhost:8085/organisations/100123/packaging-data?year=2025"
curl "http://localhost:8085/compliance-schemes/CS-004/reporting-status?submissionPeriod=2026-H1"
```

No database is needed. Nothing else has to be running.

## Endpoints

All are `GET`. There is no version prefix: nothing outside the team calls this API yet, and one can
be added back when a breaking change needs to coexist with real consumers.

| Endpoint | Answers |
|---|---|
| `/organisations/{organisationId}` | Who is this organisation, what size, which nation, in a scheme or not |
| `/organisations/{organisationId}/submissions` | What has been filed for them, newest first |
| `/organisations/{organisationId}/submissions/{submissionId}` | One submission, with validation counts and whether it has reached the warehouse |
| `/organisations/{organisationId}/packaging-data` | The reported lines, flat, filterable by `?year=` and `?status=accepted\|rejected`. The endpoint with real data behind it; see below |
| `/organisations/{organisationId}/packaging-data/summary` | Tonnage totals, broken down by material, activity and nation |
| `/compliance-schemes/{schemeId}/members` | Which producers report through this scheme |
| `/compliance-schemes/{schemeId}/packaging-data/summary` | Tonnage rolled up across the whole scheme |
| `/compliance-schemes/{schemeId}/reporting-status` | Which members have reported for a period and which have not |

Shared query parameters:

```
?submissionPeriod=2026-H1     repeatable. H1, H2 or P0 against a year
?obligationYear=2027
?material=Plastic
?submittedBy=self|scheme
?page=1&pageSize=50           pageSize is clamped to 500, not rejected
```

### Conventions worth knowing before you integrate

**The route names the organisation the data is *about*, never the one that filed it.** Who filed it
appears in the payload as `submittedBy`. That is what lets a direct producer and a compliance scheme
member share one shape rather than needing separate endpoints.

**`organisationId` is the EPR organisation reference number**, the same value that appears as
`organisation_id` in the packaging data CSV. Internal database identifiers are never exposed.

**Reporting periods are tokens, not date ranges.** Pass `2026-H1`, not a pair of dates. Large
producers report half-yearly (`H1`, `H2`), small producers annually (`P0`). The obligation year is
the year *after* the period year, and is derived rather than accepted as input. An unparseable period
returns `400` with a `ProblemDetails` body rather than an empty result that looks like "nothing was
reported".

**Nations are published as `GB-ENG`, `GB-NIR`, `GB-SCT` and `GB-WLS`.** The warehouse's `EN`, `NI`,
`SC`, `WS` form is converted at the boundary.

**Empty collections return `200` with an empty array**, never `204`.

## Response shape

Every response is wrapped, so a caller can always see how fresh the data is and where it came from.

```json
{
  "data": {
    "organisationId": "100123",
    "name": "Pop Quest Ltd",
    "type": "DirectProducer",
    "producerSize": "Large",
    "nation": "GB-ENG",
    "complianceScheme": null,
    "registration": {
      "status": "Granted",
      "referenceNumber": "EPR-2026-ENG-000123",
      "obligationYear": 2027
    }
  },
  "meta": {
    "asOf": "2026-08-13T09:00:00+00:00",
    "source": "stub",
    "page": null
  }
}
```

`meta.asOf` is **when the data was true, not when the response was built**. Today it is a fixed
fixture date; in phase two it will be the warehouse sync time. Treat data as potentially stale from
the start and phases two and three will not break you.

`meta.source` is one of `stub`, `common-data-api` or `projection`.

Collections add `meta.page`:

```json
"page": { "number": 1, "size": 50, "total": 412 }
```

### Packaging data

`/organisations/{organisationId}/packaging-data` returns one flat row per packaging line. The
submission each row came from is on the row, rather than rows being nested under submissions
(values illustrative):

```json
{
  "data": [
    {
      "organisationId": "103844",
      "subsidiaryId": "114897",
      "submissionId": "103844-2024-P1",
      "submissionPeriod": "2024-P1",
      "status": "accepted",
      "packagingActivity": "SO",
      "packagingType": "HH",
      "packagingClass": "P1",
      "packagingMaterial": "AL",
      "packagingMaterialSubtype": null,
      "packagingMaterialWeight": 76445,
      "packagingMaterialUnits": null,
      "transitionalPackagingUnits": null,
      "fromCountry": null,
      "toCountry": null,
      "ramRagRating": null
    }
  ],
  "meta": { "asOf": "2026-09-21T11:51:08+00:00", "source": "common-data-api", "page": null }
}
```

- Rows are ordered by period, then packaging type, then material, in every mode.
- `status` is `accepted`, `rejected` or `pending`, in every mode.
- A field the source does not provide is `null`, never an empty string.
- There is no per-row id. Upstream rows carry no key, and one derived from type, material and class
  collided on real data. `submissionId` is derived from organisation and period, for the same reason.
- An unknown organisation is `404`; a known one with nothing reported is `200` with an empty list.

## Where the data comes from

Endpoints depend on three interfaces and never on a data source:

```
IOrganisationProvider          Organisations/Providers/
IPackagingDataProvider         PackagingData/Providers/
IComplianceSchemeProvider      ComplianceSchemes/Providers/
```

`Shared/DataSourceRegistration.cs` is the single place that decides which implementations satisfy
them, switching on configuration:

```
DataSource__Mode = Stub | CommonDataApi | Projection
```

`Stub` and `CommonDataApi` are implemented; `Projection` throws at startup with a message naming the
phase it arrives in. In `CommonDataApi` mode only the packaging data rows come from upstream; the other
endpoints keep their stub providers, because there is no upstream equivalent for them yet. Moving to a real source is a change in that one file, not a change to any endpoint.

If you add a provider, keep its interface in domain language. The moment an interface mirrors the
shape of whatever happens to be behind it, swapping implementations stops being a registration change.

## Testing

```bash
dotnet test                                                # 121 tests
dotnet test --logger "console;verbosity=normal"            # print every test name
dotnet test --filter "FullyQualifiedName~StubOrganisationProviderTest"
dotnet test --filter "FullyQualifiedName~Health_endpoint_is_available"
```

**Nothing is mocked.** 65 unit tests construct the providers and shared types directly. The other 19
boot the real application through `WebApplicationFactory` and exercise real routing, query binding,
validation and serialisation over HTTP. In phase one the stub providers are already the test double,
so no substitution is needed.

No database is required to run the tests.

The provider tests are written as invariants rather than fixture assertions where possible, for
instance that a summary equals the sum of the lines it summarises, and that a compliance scheme
rollup never reaches outside its own membership. They are intended to double as the contract a phase
two or three adapter has to satisfy.

## Running locally

```bash
dotnet run --project EprPackagingDataArchive
```

Uses the `EprPackagingDataArchive` launch profile and listens on http://localhost:8085, serving stubs.

To run against real data instead, connect the Azure VPN and switch the data source:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:8085 \
DataSource__Mode=CommonDataApi \
CommonDataApi__BaseUrl=https://devrwdwebwa9415.azurewebsites.net \
  dotnet run --project EprPackagingDataArchive --no-launch-profile
```

Real organisations in dev include `103844` and `132926`; stub ids such as `100123` return an empty list.
Each call takes around 20 seconds, because it goes to Azure and runs a query against Synapse.

### On CDP

In dev the service runs in `CommonDataApi` mode, set in `cdp-app-config`. A protected-zone service is not
reachable from a laptop, so there are two ways to call it:

- **The CDP terminal** (Portal, service page, Terminal tab). Calls the service directly with no time limit:
  `curl -s "https://epr-packaging-data-archive.dev.cdp-int.defra.cloud/organisations/103844/packaging-data?year=2024" | jq`
- **Postman, through the developer API key endpoint.** Fine for fast endpoints, but that route gives up at
  5 seconds, so real packaging data calls fail there until the archive has its own store.

The Postman collection in this repo covers both, plus local runs.

### Docker Compose

[compose.yml](compose.yml) brings up floci (AWS emulation), Redis and MongoDB alongside the service.
**None of them are needed in phase one**, and the compose service is still named `your-backend` from
the template.

```bash
docker compose up --build -d
```

Note that the Dockerfile runs `dotnet test` inside the build stage, so a failing test fails the image
build. A more extensive local environment is available at
[github.com/DEFRA/cdp-local-environment](https://github.com/DEFRA/cdp-local-environment).

### MongoDB

Not currently used. No store is registered and the service runs without one. The template's Mongo
wiring is intact under `Utils/Mongo/` for phase three, should MongoDB be the store chosen.

In CDP environments a MongoDB instance is provisioned automatically and its credentials exposed as
environment variables.

## NuGet sources

[NuGet.config](NuGet.config) pins restore to nuget.org and clears inherited sources. If you have
DEFRA's private Azure DevOps feed configured at machine level for the Azure-hosted EPR services,
leaving it in scope makes `dotnet restore` fail with `NU1301` after several minutes, because that
feed returns 401 without a PAT. This service takes no dependency on it.

## SonarCloud

Workflow configuration exists in [.github/workflows/sonarcloud.yml](.github/workflows/sonarcloud.yml)
but is **not active**: both call sites are commented out and the project key is still the template's.
Enabling it means uncommenting them, setting `SONAR_TOKEN` and changing the key.

## Dependabot

An example configuration is included. Enable it by renaming
[.github/example.dependabot.yml](.github/example.dependabot.yml) to `.github/dependabot.yml`.

## About the licence

The Open Government Licence (OGL) was developed by the Controller of His Majesty's Stationery Office
(HMSO) to enable information providers in the public sector to license the use and re-use of their
information under a common open licence.

It is designed to encourage use and re-use of information freely and flexibly, with only a few
conditions.
