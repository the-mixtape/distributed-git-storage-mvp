# Сценарий демонстрации

**Русский** | [English](demo.en.md)

## 1. Запустить кластер

```powershell
docker compose up -d --build --wait
docker compose ps
dotnet run --project src/GitalyControlPlane.Web
```

Открыть Swagger: <http://localhost:5080/swagger>. Проверить readiness: <http://localhost:5080/health/ready>.

## 2. Создать репозиторий

```powershell
$repository = Invoke-RestMethod -Method Post `
  -Uri http://localhost:5080/repositories `
  -ContentType application/json `
  -Body '{"name":"demo"}'

$repository | ConvertTo-Json
```

Обратите внимание: ответ содержит virtual storage `cluster`, а не имя физического узла Gitaly.

## 3. Выполнить push и clone

```powershell
mkdir demo-source
git -C demo-source init
git -C demo-source commit --allow-empty -m "Initial commit"
git -C demo-source branch -M main
git -C demo-source remote add origin http://localhost:5080/demo.git
git -C demo-source push -u origin main
git clone http://localhost:5080/demo.git demo-clone
```

## 4. Показать реплики

```powershell
docker compose exec -T praefect `
  /usr/local/bin/praefect `
  -config /tmp/rendered-config/config.toml `
  metadata `
  -virtual-storage cluster `
  -relative-path $repository.relativePath
```

Все три реплики должны иметь одинаковую generation и состояние `fully up to date`.

## 5. Продемонстрировать failover

По выводу `metadata` определить текущую primary replica и остановить соответствующий контейнер Gitaly. Например:

```powershell
docker compose stop gitaly-2
git clone http://localhost:5080/demo.git failover-clone
git -C demo-source commit --allow-empty -m "Commit during outage"
git -C demo-source push origin main
docker compose start gitaly-2
docker compose up -d --wait gitaly-2
```

После завершения работы replication worker повторно выполнить команду `metadata`.

## 6. Запустить автоматические проверки

```powershell
dotnet test src/GitalyControlPlane.sln
```

Unit-тесты проверяют правила имён репозиториев, а Web-тесты — контроллеры и Problem Details без подключения к Docker.
