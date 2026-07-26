# Сценарий демонстрации C# Distributed Git Storage MVP

**Русский** | [English](demo.en.md)

## 1. Запуск

```powershell
docker compose up -d --build --wait
docker compose ps
dotnet run --project src/DistributedGitStorage.Web
```

## 2. Создание и push

Создать `demo` через Swagger или `POST /repositories`, затем выполнить Git workflow из README.

## 3. Проверка реплик

Взять `id` из ответа API, убрать дефисы и проверить SHA:

```powershell
docker compose exec -T storage-1 git -C /var/lib/git/repositories/ID.git rev-parse refs/heads/main
docker compose exec -T storage-2 git -C /var/lib/git/repositories/ID.git rev-parse refs/heads/main
docker compose exec -T storage-3 git -C /var/lib/git/repositories/ID.git rev-parse refs/heads/main
```

SHA должен совпадать.

## 4. Failover

```powershell
docker compose stop storage-1
git clone http://localhost:5080/demo.git failover-clone
git -C demo-source commit --allow-empty -m "Commit during outage"
git -C demo-source push origin main
```

В `GET /repositories` primary изменится на `storage-2`.

## 5. Восстановление

```powershell
docker compose start storage-1
docker compose up -d --wait storage-1
git -C demo-source commit --allow-empty -m "Synchronize recovered replica"
git -C demo-source push origin main
```

Повторная проверка SHA покажет одинаковое состояние трёх узлов.
