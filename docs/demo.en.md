# C# Distributed Git Storage MVP Demo

[Русский](demo.md) | **English**

## 1. Start

```powershell
docker compose up -d --build --wait
docker compose ps
dotnet run --project src/DistributedGitStorage.Web
```

## 2. Create and push

Create `demo` through Swagger or `POST /repositories`, then run the Git workflow from the README.

## 3. Verify replicas

Take the repository `id` from the API response and wait until every copy has the current generation and `Healthy` status:

```powershell
Invoke-RestMethod http://localhost:5080/repositories/ID/replicas
Invoke-RestMethod http://localhost:5080/replication/jobs
```

For a direct Git check, remove hyphens from the `id` and compare each SHA:

```powershell
docker compose exec -T storage-1 git -C /var/lib/git/repositories/ID.git rev-parse refs/heads/main
docker compose exec -T storage-2 git -C /var/lib/git/repositories/ID.git rev-parse refs/heads/main
docker compose exec -T storage-3 git -C /var/lib/git/repositories/ID.git rev-parse refs/heads/main
```

All SHAs must match.

## 4. Failover

```powershell
docker compose stop storage-1
git clone http://localhost:5080/demo.git failover-clone
git -C demo-source commit --allow-empty -m "Commit during outage"
git -C demo-source push origin main
```

After push, `GET /repositories` reports the new primary and `GET /replication/jobs` shows work for the remaining nodes.

## 5. Recovery

```powershell
docker compose start storage-1
docker compose up -d --wait storage-1
```

No new push is needed: the background worker retries automatically. The recovered copy eventually becomes `Healthy` at the current generation in `GET /repositories/ID/replicas`. Checking the SHAs again shows the same state on all three nodes.

To demonstrate stale-read protection, stop all current copies before recovery completes. `git clone` returns `503` instead of serving the lagging node.
