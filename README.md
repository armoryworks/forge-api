# forge-api

The .NET backend API for **[Forge](https://github.com/armoryworks/forge)**, a self-hosted shop-management system for small manufacturers and job shops — jobs, scheduling, parts, inventory, purchasing, sales, quality, and maintenance.

Clean-architecture solution: `forge.core` (entities/interfaces), `forge.data` (EF Core over PostgreSQL + repositories), `forge.api` (MediatR feature handlers + controllers), and `forge.integrations`. Tests live in `forge.tests`.

## Development

Requires the **.NET 10 SDK**. From the repo root:

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project forge.api      # serve the API locally
```

The schema is managed as a desired-state SQL tree in **[forge-db](https://github.com/armoryworks/forge-db)**; EF Core here is a lean query-mapping layer kept in sync by a CI drift-check.

## Deploying & Updating Forge

Forge is deployed and updated through **[`@armoryworks/forge-deploy`](https://github.com/armoryworks/forge-deploy)**, a self-contained npm package that bundles the entire deploy stack (compose files, `setup.sh`, and image pulls from GHCR). Install it once and run everything from the CLI — no repo checkout required.

**Install (Ubuntu):**

```bash
# Node.js 22 LTS (ships npm)
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
sudo apt-get install -y nodejs

# the deploy CLI
sudo npm install -g @armoryworks/forge-deploy
```

**Deploy or update a Forge install** — point it at your install directory (e.g. `/opt/forge`):

```bash
sudo npm update -g @armoryworks/forge-deploy   # pull the latest bundled deploy config
forge-deploy /opt/forge                        # unpack + run setup; pulls newer GHCR images
```

Re-running preserves your `.env`, compose overrides, and data volumes. **Docker Engine + the Compose v2 plugin** are required on the host. Full deploy, topology (split UI/API/DB), and troubleshooting docs live in the **[forge-deploy README](https://github.com/armoryworks/forge-deploy#readme)**.
