# Architecture

[Русская версия](architecture.md) | **English**

## Purpose

Gitaly Control Plane is a proof of concept for a horizontally scalable Git service. ASP.NET Core owns business metadata and public HTTP endpoints; Praefect owns repository replication and failover across physical Gitaly nodes.

## Components

```text
REST / Git client
       |
       v
GitalyControlPlane.Web
       |
       +--> GitalyControlPlane.Services --> Application PostgreSQL
       |              |
       |              +--> Praefect gRPC
       |              +--> sidechannel gateway
       |                         |
       +-------------------------+
                                 v
                              Praefect
                          /       |       \
                    Gitaly-1  Gitaly-2  Gitaly-3
```

- **Web** exposes attribute-based REST controllers, Git Smart HTTP, Problem Details and health endpoints.
- **Services** contains public application contracts and internal Gitaly implementations.
- **Data** contains the EF Core model, mapping and migrations for placement metadata.
- **Application PostgreSQL** maps a business repository ID/name to a virtual Praefect storage and relative path.
- **Praefect** chooses the primary replica and coordinates writes, replication and failover.
- **Gitaly** stores physical Git repositories. Three nodes are used in the PoC.
- **Sidechannel gateway** adapts the Gitaly Yamux sidechannel required by upload-pack in the selected Gitaly version.

## Repository creation

```text
POST /repositories
       |
       +--> validate and normalize name
       +--> choose configured virtual storage
       +--> create repository through Praefect
       +--> save placement in PostgreSQL
```

If saving placement fails, the service performs a compensating `RemoveRepository` call. This avoids leaving an untracked repository after the most common partial failure. A production system should additionally persist cleanup jobs and expose cleanup metrics.

## Git traffic

The public URL is `/{name}.git`. The application resolves the placement by name and forwards Smart HTTP operations to the configured virtual storage. The client never receives a physical Gitaly address.

## Source of truth

- Business identity and placement: application PostgreSQL.
- Git objects and refs: Gitaly repositories coordinated by Praefect.
- Replica generation and replication queue: Praefect PostgreSQL.

## Availability boundaries

The three Gitaly nodes demonstrate repository failover. The PoC still has single instances of Web, Praefect, sidechannel gateway and both PostgreSQL databases. Production requires redundant Praefect/Web instances, load balancers, HA PostgreSQL, TLS, authentication, backups and monitoring.

## Health endpoints

- `/health/live` verifies that the Web process can serve requests.
- `/health/ready` verifies application PostgreSQL, Praefect TCP connectivity and the sidechannel gateway.
