# Gitaly Control Plane

[Русский](README.md) | **English**

See the detailed [architecture](docs/architecture.en.md) and [demo runbook](docs/demo.en.md).

A working proof of concept for a custom Git control plane. ASP.NET Core stores repository placement in PostgreSQL, creates repositories in a virtual Praefect storage, and exposes Git Smart HTTP for `push`, `fetch`, and `clone`. Praefect replicates every repository across three Gitaly nodes.

## Architecture

```text
Git client / REST client
          |
          v
ASP.NET Core API ----------------------> Application PostgreSQL
          |
          +--> regular gRPC -----------+
          |                            |
          +--> sidechannel gateway ----+--> Praefect
                                               |
                                      +--------+--------+
                                      |        |        |
                                  Gitaly-1 Gitaly-2 Gitaly-3
                                      \________|________/
                                        repository replicas
                                               |
                                      Praefect PostgreSQL
```

ASP.NET stores the virtual storage name `cluster`, not a physical Gitaly node. Praefect selects the primary replica, coordinates writes, and tracks each replica generation.

The .NET solution is located at `src/GitalyControlPlane.sln`.

Gitaly 18 uses a Yamux sidechannel for `upload-pack`. A small Go adapter uses the official Gitaly client only for this transport protocol. Storage selection, placement lookup, and public Git URLs remain in ASP.NET.

## Running locally

.NET 9 and Docker Desktop running Linux containers are required.

```powershell
docker compose up -d --build --wait
dotnet restore src/GitalyControlPlane.sln
dotnet run --project src/GitalyControlPlane.Web
```

The first sidechannel gateway build downloads Gitaly sources and Go dependencies. Subsequent builds use the Docker cache.

Swagger: <http://localhost:5080/swagger>.

The Web application automatically applies pending migrations during startup.

## Creating and using a repository

Create a repository through the business API:

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5080/repositories `
  -ContentType application/json `
  -Body '{"name":"demo"}'
```

Push a commit:

```powershell
mkdir demo-source
cd demo-source
git init
git commit --allow-empty -m "Initial commit"
git branch -M main
git remote add origin http://localhost:5080/demo.git
git push -u origin main
```

Clone it:

```powershell
git clone http://localhost:5080/demo.git demo-clone
```

## Checking replicas

Take `relativePath` from the `POST /repositories` response and run:

```powershell
docker compose exec -T praefect `
  /usr/local/bin/praefect `
  -config /tmp/rendered-config/config.toml `
  metadata `
  -virtual-storage cluster `
  -relative-path poc/REPOSITORY_ID.git
```

Every replica should report the same generation and `fully up to date` state.

## Testing failover

Use the metadata command to identify the current primary and stop that Gitaly node. For example:

```powershell
docker compose stop gitaly-2
git clone http://localhost:5080/demo.git failover-clone
git -C demo-source commit --allow-empty -m "Commit during outage"
git -C demo-source push origin main
```

Praefect selects another up-to-date primary replica. Restore the node with:

```powershell
docker compose start gitaly-2
docker compose up -d --wait gitaly-2
```

The replication worker delivers the missing changes. Running `metadata` again should show the same generation on every node.

## Services and ports

| Service | Instances | Host port | Purpose |
|---|---:|---:|---|
| ASP.NET Core | 1 | 5080 | REST API and public Git Smart HTTP |
| Application PostgreSQL | 1 | 55632 | Placement catalog |
| Sidechannel gateway | 1 | 8090 | Gitaly upload-pack adapter |
| Praefect | 1 | 2305 | Virtual storage, replication, and failover |
| Praefect PostgreSQL | 1 | — | Metadata and replication queue |
| Gitaly | 3 | 8075–8077 | Three physical replicas |

The PoC runs seven Docker containers and one ASP.NET process.

## Stopping

Stop containers while retaining databases and repositories:

```powershell
docker compose down
```

Delete all test data:

```powershell
docker compose down --volumes
```

## Versions

- Gitaly, Praefect, and sidechannel client: 18.3.6;
- PostgreSQL: 16;
- ASP.NET Core: .NET 9.

Six Gitaly 18.3.6 protobuf contracts are committed as regular versioned files under `vendor/gitaly/proto`. This is intentionally not a Git submodule: the project does not need the complete Gitaly source repository, and the C# build remains reproducible without additional submodule initialization.
