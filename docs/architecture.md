# Архитектура

**Русский** | [English](architecture.en.md)

## Назначение

Gitaly Control Plane — proof of concept горизонтально масштабируемого Git-сервиса. ASP.NET Core управляет бизнес-метаданными и публичными HTTP endpoints, а Praefect отвечает за репликацию и failover репозиториев между физическими узлами Gitaly.

## Компоненты

```text
REST / Git client
       |
       v
GitalyControlPlane.Web
       |
       +--> GitalyControlPlane.Services --> Application PostgreSQL
       |              |
       |              +--> Praefect gRPC
       |              +--> sidechannel gateway
       |                         |
       +-------------------------+
                                 v
                              Praefect
                          /       |       \
                    Gitaly-1  Gitaly-2  Gitaly-3
```

- **Web** предоставляет атрибутные REST-контроллеры, Git Smart HTTP, Problem Details и health endpoints.
- **Services** содержит публичные контракты приложения и внутренние реализации интеграции с Gitaly.
- **Data** содержит EF Core-модель, конфигурацию и миграции placement-метаданных.
- **Application PostgreSQL** связывает бизнес-ID и имя репозитория с виртуальным storage Praefect и относительным путём.
- **Praefect** выбирает primary replica и координирует запись, репликацию и failover.
- **Gitaly** физически хранит Git-репозитории. В PoC используются три узла.
- **Sidechannel gateway** адаптирует Yamux sidechannel, необходимый Gitaly для `upload-pack` в используемой версии.

## Создание репозитория

```text
POST /repositories
       |
       +--> проверка и нормализация имени
       +--> выбор настроенного virtual storage
       +--> создание репозитория через Praefect
       +--> сохранение placement в PostgreSQL
```

Если placement не удалось сохранить, сервис выполняет компенсирующий вызов `RemoveRepository`. Это предотвращает появление незарегистрированного репозитория при наиболее вероятном частичном сбое. В production также потребуются постоянная очередь cleanup-задач и соответствующие метрики.

## Git-трафик

Публичный URL имеет вид `/{name}.git`. Приложение находит placement по имени и направляет Smart HTTP-операции в настроенный virtual storage. Клиент никогда не получает адрес физического Gitaly.

## Источники истины

- Бизнес-идентификатор и placement: application PostgreSQL.
- Git objects и refs: репозитории Gitaly под управлением Praefect.
- Generation реплик и очередь репликации: Praefect PostgreSQL.

## Границы доступности

Три узла Gitaly демонстрируют failover репозитория. В PoC остаются одиночные экземпляры Web, Praefect, sidechannel gateway и обеих PostgreSQL. Для production нужны несколько экземпляров Praefect/Web, load balancers, HA PostgreSQL, TLS, аутентификация, резервное копирование и мониторинг.

## Health endpoints

- `/health/live` подтверждает, что Web-процесс способен обслуживать запросы.
- `/health/ready` проверяет application PostgreSQL, TCP-доступность Praefect и sidechannel gateway.
