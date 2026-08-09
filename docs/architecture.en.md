# C# Distributed Git Storage Architecture

[Русский](architecture.md) | **English**

## Components

- **Web Control Plane** stores placement, exposes the REST API, and owns the public Git URL `/{name}.git`.
- **PostgreSQL** stores clusters and nodes, placement, current generation, replica state, and the replication queue.
- **Storage Node** stores bare repositories and exposes internal management and Git Smart HTTP endpoints.
- **System Git** performs object database, ref, and packfile operations.

## Creation

The Control Plane reads active topology from PostgreSQL, selects the active cluster with the fewest repositories, chooses its primary by round robin, creates the same bare repository on every active node in that cluster, and saves placement with `StorageClusterId`. A database failure triggers compensating deletion. Reconciliation discovers newly added active nodes and copies existing repositories belonging to their cluster.

## Reads

For `clone`, `fetch`, and `pull`, the Control Plane only selects a `Healthy` replica whose applied generation equals the repository's current generation. It checks the primary first and then other current copies. If none is available, it returns `503`; stale repository data is never served.

## Writes and replication

Before `git push`, the Control Plane acquires a PostgreSQL advisory lock scoped to the repository. This prevents two application instances from changing its refs concurrently. It then reloads placement and selects an available current replica.

Before mutating Git, a new generation is recorded and replicas are marked as awaiting synchronization. After a successful `git receive-pack`, the source refs fingerprint is persisted as current and durable replication jobs are created for the other nodes. The Control Plane then synchronously replicates and verifies copies until `WriteQuorum` is reached (2 by default). Only then is the push acknowledged.

A background worker claims jobs using a lease and runs on each target:

```text
git fetch --prune --force <source-node> +refs/*:refs/*
```

The worker verifies that source and target fingerprints match. A matching copy becomes `Healthy` at the current generation. Failures are retained with exponential backoff and the replica becomes `Lagging` or `Unavailable`. Reconciliation recreates missing work after a restart. If the original source disappears, another current healthy copy can be used.

## MVP guarantees

- concurrent writes to one repository are serialized with a PostgreSQL advisory lock;
- an acknowledged push has at least `WriteQuorum` current physical copies;
- stale copies are excluded from reads and cannot silently become primary;
- the queue survives restarts and supports leases, retries, and reconciliation;
- when an HTTP request is aborted, Git and its child processes are terminated before request resources are released;
- C# delegates the internal Git object/pack format to installed Git.

Replication up to quorum is synchronous; remaining copies are asynchronous. If quorum is not reached within `WriteQuorumTimeoutSeconds`, the client receives a Git protocol-level error containing the repository ID, generation, and current copy count. The primary may already have accepted refs, so the outcome is indeterminate until checked or safely retried; the durable queue keeps synchronizing in the background.
