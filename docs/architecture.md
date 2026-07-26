# Архитектура C# Distributed Git Storage

**Русский** | [English](architecture.en.md)

## Компоненты

- **Web Control Plane** хранит placement, предоставляет REST API и публичный Git URL `/{name}.git`.
- **PostgreSQL** связывает имя репозитория, ID и текущий primary node.
- **Storage Node** хранит bare-репозитории и предоставляет внутренние management и Git Smart HTTP endpoints.
- **System Git** выполняет операции с object database, refs и packfiles.

## Создание

Control Plane выбирает primary методом round-robin, создаёт одинаковый bare-репозиторий на всех узлах и затем сохраняет placement в PostgreSQL. При ошибке БД выполняется компенсирующее удаление.

## Чтение

Для `clone`, `fetch` и `pull` Control Plane сначала проверяет текущий primary. Если он недоступен, запрос направляется на первую healthy replica.

## Запись и репликация

`git push` направляется на доступный primary через `git receive-pack`. После успешной записи остальные узлы выполняют:

```text
git fetch --prune --force <source-node> +refs/*:refs/*
```

Недоступная replica не блокирует запись и будет синхронизирована при следующем push после восстановления. Если запись прошла через fallback node, он становится новым primary в PostgreSQL.

## Гарантии MVP

- один writer выбирается через placement;
- reachable replicas синхронизируются до завершения push;
- чтение переключается на healthy replica;
- C# не реализует внутренний Git object/pack format, а использует установленный Git.

MVP не решает конкурентные push, distributed locking, split-brain, постоянную очередь replication jobs и автоматический background reconciliation. Эти механизмы относятся к следующему production-этапу.
