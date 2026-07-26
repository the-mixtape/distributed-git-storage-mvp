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

Take the repository `id` from the API response, remove hyphens, and check each SHA:

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

`GET /repositories` now reports `storage-2` as primary.

## 5. Recovery

```powershell
docker compose start storage-1
docker compose up -d --wait storage-1
git -C demo-source commit --allow-empty -m "Synchronize recovered replica"
git -C demo-source push origin main
```

Checking the SHAs again shows the same state on all three nodes.
