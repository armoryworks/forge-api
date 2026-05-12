# Contributing to forge-api

For project-wide guidelines (branch model, PR conventions, code style),
see the umbrella repo:
**https://github.com/danielhokanson/forge/blob/main/CONTRIBUTING.md**

## Repo-specific setup

You'll need .NET 9 SDK and Docker (for Postgres).

```bash
git clone https://github.com/danielhokanson/forge-api.git
cd forge-api

# Start a Postgres for local dev (port 5432):
docker run -d --name forge \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=forge \
  -p 5432:5432 \
  postgres:17

# Restore + run
dotnet restore
dotnet run --project forge.api
```

API will start at http://localhost:5000. EF migrations auto-apply on
startup.

## Tests

```bash
dotnet build                                    # analyzers run during build
dotnet test                                     # unit + integration
dotnet test --filter "Category=Unit"            # unit only (fast)
```

Integration tests use a real Postgres (the same one above is fine) — no
mocks for the database layer.

## Adding a migration

```bash
dotnet ef migrations add MyMigrationName \
  --project forge.data \
  --startup-project forge.api
```

The "host was aborted" error at the end is expected — that's just
`dotnet ef` shutting down the host after scaffolding. The migration is
created.

## Per-repo conventions

See [`docs/coding-standards.md` in the umbrella repo](https://github.com/danielhokanson/forge/blob/main/docs/coding-standards.md)
for .NET-specific patterns: MediatR handlers, FluentValidation, Fluent
API for entity configuration, no try/catch in controllers, no "DTO"
suffix.

## Where to file what

- **API endpoint bug, business logic bug, EF/migration issue** → here
- **UI rendering bug** → file in forge-ui
- **Cross-cutting design discussion** → file in forge (umbrella)
