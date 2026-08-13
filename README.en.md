# C# Distributed Git Storage MVP

[Русский](README.md) | **English**

An MVP distributed Git storage system built with ASP.NET Core. System Git remains the storage engine, while the C# code implements the control plane, Git Smart HTTP, replication, and failover across three storage nodes.

See the detailed [architecture](docs/architecture.en.md) and [demo runbook](docs/demo.en.md).

## Features

- bare repository creation through a REST API;
- standard `git push`, `clone`, `fetch`, and `pull`;
- background replication through a durable job queue;
- generation and health tracking for every repository copy;
- reads only from current healthy replicas;
- automatic failover and recovery of lagging nodes;
- serialization of concurrent pushes to the same repository;
- write quorum: a push is acknowledged after two current physical copies exist;
- clusters, nodes, placement, replica state, and replication jobs in PostgreSQL;
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

Git and Docker Desktop running Linux containers are required for the complete stack. .NET 9 is only needed for local development outside a container.

```powershell
docker compose up -d --build --wait
```

Swagger: <http://localhost:5080/swagger>
Readiness: <http://localhost:5080/health/ready>

The Web application automatically applies pending EF Core migrations during startup.

Replica state is exposed by `GET /repositories/{id}/replicas`, and the queue by
`GET /replication/jobs`. A failed job can be retried manually with
`POST /replication/jobs/{id}/retry`.

Storage topology is no longer configured in `appsettings.json`. Clusters and nodes
are stored in PostgreSQL and exposed by `GET /storage-clusters`. They can be managed
with `POST /storage-clusters`, `PUT /storage-clusters/{id}`,
`POST /storage-clusters/{id}/nodes`, and `PUT /storage-clusters/{id}/nodes/{nodeId}`.

The topology is empty after the first startup. Create a cluster and add its nodes explicitly before creating repositories:

```powershell
$cluster = Invoke-RestMethod -Method Post `
  -Uri http://localhost:5080/storage-clusters `
  -ContentType application/json `
  -Body '{"name":"main-cluster"}'

1..3 | ForEach-Object {
  $name = "storage-$_"
  Invoke-RestMethod -Method Post `
    -Uri "http://localhost:5080/storage-clusters/$($cluster.id)/nodes" `
    -ContentType application/json `
    -Body (@{
      name = $name
      address = "http://${name}:8080"
      internalAddress = "http://${name}:8080"
    } | ConvertTo-Json)
}
```

Until at least one active node is configured, `/health/ready` correctly reports that the service is not ready.

`Replication:WriteQuorum` defaults to `2`, bounded by
`Replication:WriteQuorumTimeoutSeconds`. If quorum cannot be reached, the client does
not receive a successful acknowledgement, while persisted jobs continue recovery in the background.

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
| ASP.NET Core Control Plane | 1 | 5080 | REST API and public Git Smart HTTP; runs in Compose |
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
