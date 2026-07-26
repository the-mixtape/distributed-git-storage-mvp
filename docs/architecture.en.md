# C# Distributed Git Storage Architecture

[Русский](architecture.md) | **English**

## Components

- **Web Control Plane** stores placement, exposes the REST API, and owns the public Git URL `/{name}.git`.
- **PostgreSQL** maps repository name and ID to the current primary node.
- **Storage Node** stores bare repositories and exposes internal management and Git Smart HTTP endpoints.
- **System Git** performs object database, ref, and packfile operations.

## Creation

The Control Plane selects a primary using round-robin placement, creates the same bare repository on every node, and then saves placement in PostgreSQL. A database failure triggers compensating deletion.

## Reads

For `clone`, `fetch`, and `pull`, the Control Plane checks the current primary first. If it is unavailable, the request is routed to the first healthy replica.

## Writes and replication

`git push` is routed to an available primary through `git receive-pack`. After a successful write, the other nodes execute:

```text
git fetch --prune --force <source-node> +refs/*:refs/*
```

An unavailable replica does not block the write and is synchronized by the next push after recovery. If the write used a fallback node, that node becomes the new primary in PostgreSQL.

## MVP guarantees

- placement selects a single writer;
- reachable replicas synchronize before push completes;
- reads fail over to a healthy replica;
- C# delegates the internal Git object/pack format to installed Git.

The MVP does not provide concurrent-push coordination, distributed locking, split-brain prevention, a durable replication job queue, or automatic background reconciliation. Those belong to a production phase.
