# Demo runbook

[Русская версия](demo.md) | **English**

## 1. Start the cluster

```powershell
docker compose up -d --build --wait
docker compose ps
dotnet run --project src/GitalyControlPlane.Web
```

Open Swagger at <http://localhost:5080/swagger> and readiness at <http://localhost:5080/health/ready>.

## 2. Create a repository

```powershell
$repository = Invoke-RestMethod -Method Post `
  -Uri http://localhost:5080/repositories `
  -ContentType application/json `
  -Body '{"name":"demo"}'

$repository | ConvertTo-Json
```

Point out that the response contains virtual storage `cluster`, not a physical Gitaly node.

## 3. Push and clone

```powershell
mkdir demo-source
git -C demo-source init
git -C demo-source commit --allow-empty -m "Initial commit"
git -C demo-source branch -M main
git -C demo-source remote add origin http://localhost:5080/demo.git
git -C demo-source push -u origin main
git clone http://localhost:5080/demo.git demo-clone
```

## 4. Show replicas

```powershell
docker compose exec -T praefect `
  /usr/local/bin/praefect `
  -config /tmp/rendered-config/config.toml `
  metadata `
  -virtual-storage cluster `
  -relative-path $repository.relativePath
```

All three replicas should be fully up to date with the same generation.

## 5. Demonstrate failover

Use the metadata output to identify the current primary, then stop that Gitaly container. For example:

```powershell
docker compose stop gitaly-2
git clone http://localhost:5080/demo.git failover-clone
git -C demo-source commit --allow-empty -m "Commit during outage"
git -C demo-source push origin main
docker compose start gitaly-2
docker compose up -d --wait gitaly-2
```

Run the metadata command again after the replication worker catches up.

## 6. Run automated checks

```powershell
dotnet test src/GitalyControlPlane.sln
```

The unit suite checks repository-name rules; the Web suite checks controllers and Problem Details without requiring Docker.
