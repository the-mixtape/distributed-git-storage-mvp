# Production deployment template

Это отдельный шаблон для корпоративного Kubernetes. Он не использует Minikube hostPath и не содержит паролей. Перед применением значения необходимо адаптировать под инфраструктуру компании.

## Обязательные настройки

1. В `kustomization.yaml` заменить registry и версию двух образов.
2. В `storage-nodes.yaml` заменить `company-rwo-retain` на корпоративный StorageClass с `ReadWriteOnce`. Для PV должна быть настроена reclaim policy `Retain`.
3. Проверить размер PVC каждой storage-ноды — по умолчанию `500Gi`.
4. В `ingress.yaml` заменить `git.company.example`, `ingressClassName` и имя TLS Secret.
5. При закрытом registry добавить `imagePullSecrets` в pod templates.
6. Согласовать requests/limits с доступными ресурсами кластера.

## Образы

Собрать и отправить образы с неизменяемым version tag:

```powershell
docker build -t registry.company.example/distributed-git-storage/control-plane:0.1.0 `
  -f src/DistributedGitStorage.Web/Dockerfile .
docker build -t registry.company.example/distributed-git-storage/storage-node:0.1.0 `
  -f src/DistributedGitStorage.StorageNode/Dockerfile .

docker push registry.company.example/distributed-git-storage/control-plane:0.1.0
docker push registry.company.example/distributed-git-storage/storage-node:0.1.0
```

## Secrets

PostgreSQL предоставляется инфраструктурой компании. Secret со строкой подключения не хранится в Git и создаётся отдельно:

```powershell
kubectl create namespace distributed-git-storage --dry-run=client -o yaml | kubectl apply -f -

kubectl create secret generic database-connection `
  --namespace distributed-git-storage `
  --from-literal=connection-string='Host=POSTGRES_HOST;Port=5432;Database=gitserver;Username=APP_USER;Password=REPLACE_WITH_STRONG_PASSWORD;SSL Mode=Require;Trust Server Certificate=false'
```

TLS Secret создаётся корпоративным cert-manager или администратором кластера под именем `distributed-git-storage-tls`.

## Проверка и развёртывание

До применения проверить итоговый YAML:

```powershell
kubectl kustomize deploy/prod
kubectl apply --dry-run=server -k deploy/prod
```

Развернуть:

```powershell
kubectl apply -k deploy/prod
kubectl rollout status statefulset/storage-node -n distributed-git-storage --timeout=300s
kubectl rollout status deployment/control-plane -n distributed-git-storage --timeout=300s
kubectl get pods,pvc,pv,ingress -n distributed-git-storage
```

После запуска явно зарегистрировать кластер и адреса StatefulSet-нод через API:

```text
http://storage-node-0.storage-node.distributed-git-storage.svc.cluster.local:8080
http://storage-node-1.storage-node.distributed-git-storage.svc.cluster.local:8080
http://storage-node-2.storage-node.distributed-git-storage.svc.cluster.local:8080
```

## Ограничения текущего шаблона

- Доступность, TLS, backup и восстановление PostgreSQL обеспечиваются инфраструктурой компании. Приложение получает только connection string через Kubernetes Secret.
- Control Plane оставлен в одном экземпляре, чтобы несколько pod одновременно не применяли EF migrations при старте. Перед горизонтальным масштабированием migrations нужно вынести в отдельный Job/этап deployment.
- TLS закрывает транспорт, но в приложении пока нет пользовательской аутентификации и авторизации. До их реализации доступ к Ingress должен быть ограничен сетью или внешним auth proxy.
- `Retain` предотвращает автоматическое удаление PV, но не заменяет snapshots и внешний backup PostgreSQL и Git-репозиториев.
