# Gitaly Control Plane

**Русский** | [English](README.en.md)

Подробности: [архитектура](docs/architecture.md) и [сценарий демонстрации](docs/demo.md).

Рабочий proof of concept собственного Git control plane. ASP.NET Core хранит placement в PostgreSQL, создаёт репозитории в виртуальном Praefect storage и предоставляет Git Smart HTTP для `push`, `fetch` и `clone`. Praefect реплицирует каждый репозиторий на три Gitaly.

## Архитектура

```text
Git client / REST client
          |
          v
ASP.NET Core API ----------------------> Application PostgreSQL
          |
          +--> regular gRPC -----------+
          |                            |
          +--> sidechannel gateway ----+--> Praefect
                                               |
                                      +--------+--------+
                                      |        |        |
                                  Gitaly-1 Gitaly-2 Gitaly-3
                                      \________|________/
                                        repository replicas
                                               |
                                      Praefect PostgreSQL
```

ASP.NET хранит имя virtual storage `cluster`, а не физический Gitaly. Praefect выбирает primary, координирует запись и отслеживает generation каждой реплики.

.NET solution находится в `src/GitalyControlPlane.sln`.

Gitaly 18 использует Yamux sidechannel для `upload-pack`. Маленький Go-адаптер использует официальный клиент Gitaly только для этого транспортного протокола. Выбор storage, lookup placement и публичный Git URL остаются в ASP.NET.

## Запуск

Требуются .NET 9 и Docker Desktop в режиме Linux containers.

```powershell
docker compose up -d --build --wait
dotnet restore src/GitalyControlPlane.sln
dotnet run --project src/GitalyControlPlane.Web
```

Первая сборка sidechannel gateway скачивает исходники и Go-зависимости Gitaly. Последующие сборки используют Docker cache.

Swagger: <http://localhost:5080/swagger>.

При запуске Web-приложение автоматически применяет ожидающие миграции.

## Создание и использование репозитория

Создать репозиторий через business API:

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5080/repositories `
  -ContentType application/json `
  -Body '{"name":"demo"}'
```

Отправить commit:

```powershell
mkdir demo-source
cd demo-source
git init
git commit --allow-empty -m "Initial commit"
git branch -M main
git remote add origin http://localhost:5080/demo.git
git push -u origin main
```

Клонировать:

```powershell
git clone http://localhost:5080/demo.git demo-clone
```

## Проверка реплик

Возьмите `relativePath` из ответа `POST /repositories` и выполните:

```powershell
docker compose exec -T praefect `
  /usr/local/bin/praefect `
  -config /tmp/rendered-config/config.toml `
  metadata `
  -virtual-storage cluster `
  -relative-path poc/REPOSITORY_ID.git
```

Для каждой реплики должны отображаться одинаковая generation и состояние `fully up to date`:

```text
Replicas:
- Storage: "gitaly-1"
  Generation: 1, fully up to date
- Storage: "gitaly-2"
  Generation: 1, fully up to date
- Storage: "gitaly-3"
  Generation: 1, fully up to date
```

## Проверка failover

Узнать текущий primary можно командой `metadata` выше. Остановите именно этот Gitaly, например:

```powershell
docker compose stop gitaly-2
git clone http://localhost:5080/demo.git failover-clone
git -C demo-source commit --allow-empty -m "Commit during outage"
git -C demo-source push origin main
```

Praefect выберет новую актуальную primary replica. Вернуть узел:

```powershell
docker compose start gitaly-2
docker compose up -d --wait gitaly-2
```

Replication worker автоматически доставит пропущенные изменения. Повторная команда `metadata` должна показать одинаковую generation на всех узлах.

## Сервисы и порты

| Сервис | Экземпляров | Host port | Назначение |
|---|---:|---:|---|
| ASP.NET Core | 1 | 5080 | REST API и публичный Git Smart HTTP |
| Application PostgreSQL | 1 | 55632 | каталог placement |
| Sidechannel gateway | 1 | 8090 | адаптер Gitaly upload-pack |
| Praefect | 1 | 2305 | virtual storage, replication и failover |
| Praefect PostgreSQL | 1 | — | metadata и replication queue |
| Gitaly | 3 | 8075–8077 | три физические реплики |

Всего в POC: семь Docker-контейнеров и один ASP.NET-процесс.

## Остановка

Остановить контейнеры с сохранением БД и репозиториев:

```powershell
docker compose down
```

Удалить все тестовые данные:

```powershell
docker compose down --volumes
```

Если окружение использовалось до добавления Praefect, старые direct-Gitaly placement и репозитории автоматически в кластер не мигрируют. Для чистой проверки удалите volumes и создайте репозитории заново.

## Версии

- Gitaly, Praefect и sidechannel client: 18.3.6;
- PostgreSQL: 16;
- ASP.NET Core: .NET 9.

Шесть protobuf-контрактов Gitaly 18.3.6 сохранены как обычные versioned-файлы в `vendor/gitaly/proto`. Это намеренно не Git submodule: проекту не требуется полный исходный репозиторий Gitaly, а сборка C# должна воспроизводиться без дополнительной инициализации submodules.
