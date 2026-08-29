# forge-api

[![CI](https://github.com/armoryworks/forge-api/actions/workflows/ci.yml/badge.svg)](https://github.com/armoryworks/forge-api/actions/workflows/ci.yml)

The .NET backend for **Forge** — free, open-source ERP and MES for job shops: job cards on the floor, books in the office, one database.

This repository is one piece of the Forge product family. It holds the API server: the HTTP surface the Angular frontend and the mobile app talk to, the domain logic behind it, and the background jobs and real-time hubs that keep a shop floor current. It does not hold the database schema (that is [forge-db](https://github.com/armoryworks/forge-db)) or the deploy stack (that is [forge-deploy](https://github.com/armoryworks/forge-deploy)).

- **Umbrella repo and project docs:** [github.com/armoryworks/forge](https://github.com/armoryworks/forge)
- **Product site:** [armoryworks.com](https://armoryworks.com)
- **Install Forge:** start at [forge-deploy](https://github.com/armoryworks/forge-deploy) — you do not need this repo to run Forge.

---

## Solution layout

.NET 10, five projects, referenced through `forge.slnx`:

| Project | What belongs in it |
|---|---|
| `forge.core` | Entities, enums, interfaces, value models, settings records. No EF, no ASP.NET, no HTTP — the vocabulary every other project shares. |
| `forge.data` | EF Core over PostgreSQL (Npgsql + pgvector): `AppDbContext`, entity configuration, repositories, save interceptors, and `SchemaBootstrapper` with the embedded schema SQL. |
| `forge.api` | The web host. MediatR command/query handlers under `Features/`, thin controllers, middleware, SignalR hubs, Hangfire jobs, the capability catalog and gate, seed data. |
| `forge.integrations` | Outbound adapters to the world — accounting (QuickBooks, Xero, Sage, NetSuite, …), shipping carriers, storage (MinIO / local / cloud drives), SMTP, messaging, AI, PDF. Each is an interface with a real implementation and a mock twin. |
| `forge.tests` | xUnit tests for all of the above. |

The dependency direction is one-way: `core` ← `data`, `core` ← `integrations`, and `api` on top of all three.

Inside `forge.api`, the shape is CQRS with MediatR: a request record, a result record, a handler, and a FluentValidation validator per operation, grouped by feature folder. Controllers stay thin and delegate; a global exception middleware maps exceptions to RFC 7807 Problem Details, so controllers carry no `try`/`catch`.

## Prerequisites

- **.NET 10 SDK** (`global.json` pins `10.0.100` with `rollForward: latestFeature`)
- **PostgreSQL 17 with the `pgvector` extension** — the schema declares `CREATE EXTENSION vector`, so a stock `postgres` image will not boot the app. Use `pgvector/pgvector:pg17`.
- **Docker** — required for the Postgres-backed test collection, and the easiest way to get the database above.

## Quickstart

```bash
git clone https://github.com/armoryworks/forge-api.git
cd forge-api

# PostgreSQL with pgvector
docker run -d --name forge-db \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=forge \
  -p 5432:5432 \
  pgvector/pgvector:pg17

# A JWT signing key is mandatory — the app refuses to start without one,
# and it must be at least 32 characters.
export Jwt__Key="$(head -c 48 /dev/urandom | base64)"

dotnet restore
dotnet build
dotnet run --project forge.api
```

The API listens on **http://localhost:5000** (the `http` launch profile). On first boot it applies the schema, seeds reference data, seeds the capability catalog, and hydrates the capability snapshot.

| Endpoint | Notes |
|---|---|
| `GET /api/v1/health` | Aggregate health: Postgres, Hangfire, MinIO, SignalR |
| `/scalar` | Interactive API reference — Development environment only |
| `/openapi/v1.json` | OpenAPI document — Development environment only |
| `/hangfire` | Background-job dashboard, Admin role required |
| `/hubs/board`, `/hubs/notifications`, `/hubs/timer`, `/hubs/chat`, `/hubs/accounting` | SignalR |

### Configuration

Settings come from `appsettings.json`, an optional gitignored `appsettings.Secrets.json`, and environment variables. Nested keys use the standard double-underscore form (`Jwt__Key`, `ConnectionStrings__DefaultConnection`).

| Key | Purpose |
|---|---|
| `Jwt__Key` | JWT signing key, 32+ characters. **Required** — startup throws without it. |
| `ConnectionStrings__DefaultConnection` | Defaults to `Host=localhost;Port=5432;Database=forge;Username=postgres;Password=postgres`. |
| `MockIntegrations` | `true` makes every external integration return canned data. On in Development. |
| `RECREATE_DB` | `true` **drops and recreates the database** at boot. |
| `SEED_DEMO_DATA` / `SEED_USER_PASSWORD` | Seed demo users, customers, and jobs (`admin@forge.local` et al.). Both are needed; demo seeding refuses to run in Production unless `ALLOW_DEMO_DATA_IN_PRODUCTION=true`. |
| `CORS_ORIGINS` | Comma-separated extra origins, added to the localhost and in-cluster defaults. |
| `Seq__ServerUrl` | Optional Serilog sink; console logging is always on. |

> **`appsettings.Development.json` ships `RECREATE_DB: true`.** Running in the Development environment therefore wipes the database on every start, which is what you want for a scratch dev loop and emphatically not what you want against data you care about. Set `RECREATE_DB=false` to keep your data.

## Database schema

**The schema is owned by [forge-db](https://github.com/armoryworks/forge-db), not by EF Core. There are no EF migrations in this repository — do not add any, and do not run `dotnet ef migrations add`.**

forge-db keeps the desired state as a SQL tree and diffs it with [pg-schema-diff](https://github.com/stripe/pg-schema-diff). That tree is assembled into a single ordered DDL file, committed here as the embedded resource `forge.data/Schema/forge-schema.sql`, which includes the parts EF's model cannot express — extensions, functions, and the ledger immutability triggers. At boot, `SchemaBootstrapper` applies that file to a fresh database and is a no-op against an existing one.

EF Core is left as a lean query-mapping layer. Entity mapping prefers data annotations; `OnModelCreating` is reserved for what attributes cannot express, such as the soft-delete global query filter. `PendingModelChangesWarning` is suppressed on purpose, because forge-db's naming is the authority.

When forge-db's schema changes, regenerate the embedded artifact:

```bash
forge-db assemble --repo <path-to-forge-db> --out forge.data/Schema/forge-schema.sql
```

The `Schema drift check` workflow (run on demand) re-assembles forge-db and fails if the committed SQL has drifted. The complementary invariant — that the EF model still maps onto that schema — is covered by the Postgres-backed tests, which run real queries against the same schema the application boots.

## Tests

```bash
dotnet test                          # everything
dotnet test --filter Architecture    # the architecture ratchet tests only
```

Three kinds of test live in `forge.tests`:

- **Unit and in-memory integration tests.** Most of the suite. These use the EF Core in-memory provider and a `WebApplicationFactory` with Hangfire on memory storage; no external services needed.
- **The Postgres-backed collection.** Roughly three dozen test classes that need real PostgreSQL semantics — filtered unique indexes, `ExecuteUpdate`, pgvector columns, ledger triggers. `PostgresFixture` starts a `pgvector/pgvector:pg17` container through Testcontainers and applies the forge-db schema once per collection, **so a reachable Docker daemon is required.** If Testcontainers cannot reach your daemon, point the fixture at a Postgres you started yourself:

  ```bash
  export FORGE_TEST_PG="Host=localhost;Port=55432;Database=forge_test;Username=forge;Password=forgetest"
  dotnet test
  ```

- **Architecture ratchet tests** (`forge.tests/Architecture/`). These promote coding standards to failing tests with a per-file baseline: inject `IClock` rather than reading `DateTime.UtcNow`, no `try`/`catch` in controllers, no file with five or more top-level types, and every capability-gated controller accounted for. New files must be clean; baselined files may not get worse.

CI runs restore, `dotnet build -warnaserror`, and the full suite against a Postgres service container on every push and PR to `main` and `develop`.

## Capabilities

Forge is one codebase that fits many shops, so features are gated per install rather than shipped as separate editions. A static catalog names every capability (`CAP-MD-CUSTOMERS`, `CAP-INV-LOTS`, `CAP-ACCT-FULLGL`, …); each install stores its own on/off state in the `capabilities` table.

**`forge.api/Capabilities/CapabilityCatalog.cs` is the source of truth.** It holds 173 capabilities at the time of writing, of which 62 are default-on — count it yourself rather than trusting that number:

```bash
grep -c 'new("CAP-' forge.api/Capabilities/CapabilityCatalog.cs
```

Enforcement happens in two places, because not every code path is an HTTP request:

- `CapabilityGateMiddleware` reads `[RequiresCapability("CAP-…")]` off the matched endpoint and short-circuits with `403` plus an `X-Capability-Disabled` header when the capability is off.
- `CapabilityGateBehavior`, a MediatR pipeline behavior, applies the same check to handlers invoked outside a request — Hangfire jobs, for instance.

Endpoints marked `[CapabilityBootstrap]` — authentication, the capability descriptor, capability administration — always pass, so an operator cannot lock themselves out by turning something off. A seeder upserts the catalog at startup and refreshes metadata only; it never overwrites an install's `Enabled` column, because that state belongs to the operator.

Related pieces: `CapabilityCatalogRelations.cs` declares dependency edges and mutexes evaluated at toggle time, `ModuleCatalog.cs` groups capabilities into the plain-language modules a first-run picker offers, and `CapabilitiesController` exposes the admin surface at `/api/v1/capabilities` (descriptor, per-code toggle, bulk toggle, validate, relations, audit log).

Every new endpoint either reuses an existing capability or registers a new one in the catalog before it ships.

## What else is wired

- **MediatR** with a validation behavior and the capability behavior in the pipeline; **FluentValidation** validators registered by assembly scan.
- **ASP.NET Identity + JWT**, with optional Google / Microsoft / OIDC single sign-on, TOTP and passkey (WebAuthn) MFA, device-token auth for shop-floor kiosk terminals, and two API-key schemes — one read-only for BI tools, one user-bound for integrations.
- **SignalR** for the kanban board, notifications, shop-floor timers, chat, and accounting refreshes.
- **Hangfire** on PostgreSQL storage for background and recurring work, dashboard gated to the Admin role.
- **Serilog** structured logging to console, with an optional Seq sink.
- Global rate limiting (off in Development and for loopback callers), security headers, forwarded headers, idempotency, shared-device, and audit-context middleware.

## Deploying

Forge is deployed and updated through **[`@armoryworks/forge-deploy`](https://github.com/armoryworks/forge-deploy)**, a self-contained npm package that bundles the whole deploy stack — compose files, `setup.sh`, and image pulls from GHCR. Install it once and run everything from the CLI; no repo checkout required.

```bash
# Ubuntu: Node.js 22 LTS (ships npm)
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
sudo apt-get install -y nodejs

# the deploy CLI
sudo npm install -g @armoryworks/forge-deploy

# deploy or update an install, pointed at its directory
sudo npm update -g @armoryworks/forge-deploy
forge-deploy /opt/forge
```

Re-running preserves your `.env`, compose overrides, and data volumes. Docker Engine and the Compose v2 plugin are required on the host. Full deploy, topology (split UI / API / DB), and troubleshooting docs live in the [forge-deploy README](https://github.com/armoryworks/forge-deploy#readme).

Container images for this repo are built by the release workflows and published to GHCR as `ghcr.io/armoryworks/forge-api`, multi-arch (amd64 + arm64). The runtime image listens on port 8080.

## Contributing

The project-wide branch model, PR conventions, and coding standards live in the [umbrella repo's CONTRIBUTING](https://github.com/armoryworks/forge/blob/main/CONTRIBUTING.md); [CONTRIBUTING.md](CONTRIBUTING.md) here covers repo-specific notes. Local setup is the Quickstart above.

Where to file an issue:

- API endpoints, business logic, EF or schema-mapping problems → [this repo](https://github.com/armoryworks/forge-api/issues)
- UI rendering and frontend behavior → [forge-ui](https://github.com/armoryworks/forge-ui/issues)
- Schema definitions and migrations → [forge-db](https://github.com/armoryworks/forge-db/issues)
- Install, upgrade, and compose problems → [forge-deploy](https://github.com/armoryworks/forge-deploy/issues)
- Cross-cutting design discussion → [forge](https://github.com/armoryworks/forge/issues)

## License

Apache License 2.0 — see [LICENSE](LICENSE) and [NOTICE](NOTICE).

Copyright 2026 Armory Works Technology, LLC.
