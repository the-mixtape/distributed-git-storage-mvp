# C# Distributed Git Storage MVP

**Русский** | [English](README.en.md)

MVP собственного распределённого Git-хранилища на ASP.NET Core. Системный Git используется как storage engine, а C#-код реализует control plane, Git Smart HTTP, репликацию и failover между тремя storage nodes.

Подробности: [архитектура](docs/architecture.md) и [сценарий демонстрации](docs/demo.md).

## Возможности

- создание bare-репозитория через REST API;
- стандартные `git push`, `clone`, `fetch` и `pull`;
- синхронная репликация refs на три C# storage node;
- автоматический выбор доступной реплики;
- смена primary после записи во время отказа;
- восстановление отставшего узла при следующей записи;
- placement metadata в PostgreSQL;
- health checks и автоматические тесты.

## Архитектура

```text
Git / REST client
        |
        v
ASP.NET Core Control Plane ------> PostgreSQL
        |
        +--------+---------+
        |        |         |
        v        v         v
   Storage-1 Storage-2 Storage-3
       |        |         |
       +--------+---------+
          bare Git repositories
```

Каждый storage node — ASP.NET Core-приложение, запускающее безопасно аргументированные процессы `git init --bare`, `git upload-pack`, `git receive-pack` и `git fetch`.

## Запуск

Требуются .NET 9, Git и Docker Desktop в режиме Linux containers.

```powershell
docker compose up -d --build --wait
dotnet restore src/DistributedGitStorage.sln
dotnet run --project src/DistributedGitStorage.Web
```

Swagger: <http://localhost:5080/swagger>
Readiness: <http://localhost:5080/health/ready>

При запуске Web-приложение автоматически применяет ожидающие EF Core migrations.

## Создание репозитория

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5080/repositories `
  -ContentType application/json `
  -Body '{"name":"demo"}'
```

## Git workflow

```powershell
mkdir demo-source
git -C demo-source init
git -C demo-source config user.name "Demo User"
git -C demo-source config user.email "demo@example.com"
"Hello" | Set-Content demo-source/README.md
git -C demo-source add README.md
git -C demo-source commit -m "Initial commit"
git -C demo-source branch -M main
git -C demo-source remote add origin http://localhost:5080/demo.git
git -C demo-source push -u origin main

git clone http://localhost:5080/demo.git demo-clone
```

## Сервисы и порты

| Сервис | Экземпляров | Host port | Назначение |
|---|---:|---:|---|
| ASP.NET Core Control Plane | 1 | 5080 | REST API и публичный Git Smart HTTP |
| PostgreSQL | 1 | 55632 | placement metadata |
| C# Storage Node | 3 | 8081–8083 | bare Git repositories и внутренний Smart HTTP |

## Тесты

```powershell
dotnet test src/DistributedGitStorage.sln
```

## Остановка

Сохранить данные:

```powershell
docker compose down
```

Удалить тестовые БД и репозитории:

```powershell
docker compose down --volumes
```
