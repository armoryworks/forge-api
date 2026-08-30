# Contributing to forge-api

For project-wide guidelines (branch model, PR conventions, code style),
see the umbrella repo:
**https://github.com/armoryworks/forge/blob/main/CONTRIBUTING.md**

## Repo-specific setup

You'll need the **.NET 10 SDK** (pinned in `global.json`) and Docker, for Postgres.

```bash
git clone https://github.com/armoryworks/forge-api.git
cd forge-api

# Postgres for local dev. The pgvector image is required, not optional:
# the schema creates the `vector` extension and the API will not boot without it.
docker run -d --name forge \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=forge \
  -p 5432:5432 \
  pgvector/pgvector:pg18

dotnet restore
Jwt__Key="a-local-dev-key-at-least-32-characters" dotnet run --project forge.api
```

`Jwt__Key` is mandatory — the app throws on startup without at least 32
characters. The API listens on http://localhost:5000.

**In Development the database is recreated on every start**, so treat local
data as disposable.

## The schema is not managed here

There are **no EF Core migrations in this repo, and you should not add any.**
The schema is desired-state SQL owned by
[forge-db](https://github.com/armoryworks/forge-db); `SchemaBootstrapper`
applies the embedded `forge.data/Schema/forge-schema.sql` to a fresh database
and is a no-op on an existing one. EF Core here is a lean query-mapping layer.

To change the schema: make the change in forge-db, then regenerate the embedded
SQL. Running `dotnet ef migrations add` will produce something this project
cannot use.

## Tests

```bash
dotnet build --configuration Release -warnaserror   # warnings fail the build
dotnet test                                         # the whole suite
dotnet test --filter Architecture                   # the standards/ratchet tests
```

Tests that need a database spin up their own Postgres via Testcontainers, so
Docker must be running and reachable. There are no `Category` traits — a
`--filter "Category=Unit"` matches nothing.

## Per-repo conventions

See [`docs/coding-standards.md` in the umbrella repo](https://github.com/armoryworks/forge/blob/main/docs/coding-standards.md)
for .NET-specific patterns: MediatR handlers, FluentValidation, entity
configuration, no try/catch in controllers, no "DTO" suffix.

Two rules the build enforces: every controller carries a capability attribute,
and every mutating handler writes an activity-log row.

## Where to file what

- **API endpoint bug, business logic bug** → here
- **Schema change or database structure** → [forge-db](https://github.com/armoryworks/forge-db)
- **UI rendering bug** → [forge-ui](https://github.com/armoryworks/forge-ui)
- **Deployment / hosting** → [forge-deploy](https://github.com/armoryworks/forge-deploy)
- **Cross-cutting design discussion** → [forge](https://github.com/armoryworks/forge) (umbrella)
