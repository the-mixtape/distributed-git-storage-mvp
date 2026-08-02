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

Взять `id` из ответа API и дождаться, пока все копии получат текущее поколение и статус `Healthy`:

```powershell
Invoke-RestMethod http://localhost:5080/repositories/ID/replicas
Invoke-RestMethod http://localhost:5080/replication/jobs
```

Для непосредственной проверки Git убрать дефисы из `id` и сравнить SHA:

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

После push новая primary-нода сохраняется в `GET /repositories`, а задания для остальных нод появляются в `GET /replication/jobs`.

## 5. Восстановление

```powershell
docker compose start storage-1
docker compose up -d --wait storage-1
```

Новый push не требуется: background worker автоматически повторит задание. В `GET /repositories/ID/replicas` восстановленная копия перейдёт в `Healthy` и получит текущее поколение. Повторная проверка SHA покажет одинаковое состояние трёх узлов.

Чтобы продемонстрировать защиту от устаревшего чтения, можно остановить все актуальные копии до завершения восстановления. `git clone` получит `503`, но не данные от отставшей ноды.
