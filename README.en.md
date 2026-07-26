# C# Distributed Git Storage MVP

[Русский](README.md) | **English**

An MVP distributed Git storage system built with ASP.NET Core. System Git remains the storage engine, while the C# code implements the control plane, Git Smart HTTP, replication, and failover across three storage nodes.

See the detailed [architecture](docs/architecture.en.md) and [demo runbook](docs/demo.en.md).

## Features

- bare repository creation through a REST API;
- standard `git push`, `clone`, `fetch`, and `pull`;
- synchronous ref replication across three C# storage nodes;
- automatic selection of an available replica;
- primary promotion after a write during an outage;
- recovery of a stale node on the next write;
- placement metadata in PostgreSQL;
- health checks and automated tests.

## Architecture

```text
Git / REST client
        |
        v
ASP.NET Core Control Plane ------> PostgreSQL
        |
        +--------+---------+
        |        |         |
        v        v         v
   Storage-1 Storage-2 Storage-3
       |        |         |
       +--------+---------+
          bare Git repositories
```

Each storage node is an ASP.NET Core application that runs safely argumentized `git init --bare`, `git upload-pack`, `git receive-pack`, and `git fetch` processes.

## Running locally

.NET 9, Git, and Docker Desktop running Linux containers are required.

```powershell
docker compose up -d --build --wait
dotnet restore src/DistributedGitStorage.sln
dotnet run --project src/DistributedGitStorage.Web
```

Swagger: <http://localhost:5080/swagger>
Readiness: <http://localhost:5080/health/ready>

The Web application automatically applies pending EF Core migrations during startup.

## Create a repository

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5080/repositories `
  -ContentType application/json `
  -Body '{"name":"demo"}'
```

## Git workflow

```powershell
mkdir demo-source
git -C demo-source init
git -C demo-source config user.name "Demo User"
git -C demo-source config user.email "demo@example.com"
"Hello" | Set-Content demo-source/README.md
git -C demo-source add README.md
git -C demo-source commit -m "Initial commit"
git -C demo-source branch -M main
git -C demo-source remote add origin http://localhost:5080/demo.git
git -C demo-source push -u origin main

git clone http://localhost:5080/demo.git demo-clone
```

## Services and ports

| Service | Instances | Host port | Purpose |
|---|---:|---:|---|
| ASP.NET Core Control Plane | 1 | 5080 | REST API and public Git Smart HTTP |
| PostgreSQL | 1 | 55632 | Placement metadata |
| C# Storage Node | 3 | 8081–8083 | Bare Git repositories and internal Smart HTTP |

## Tests

```powershell
dotnet test src/DistributedGitStorage.sln
```

## Stopping

Keep the data:

```powershell
docker compose down
```

Delete test databases and repositories:

```powershell
docker compose down --volumes
```
