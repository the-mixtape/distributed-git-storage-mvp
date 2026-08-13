# Запуск в Minikube

Стенд использует StatefulSet для PostgreSQL и трёх storage-нод. Каждая storage-нода получает собственный PVC; пересозданный pod с тем же порядковым номером повторно подключает прежний volume.

## Развёртывание

```powershell
tools\minikube.exe start --driver=docker --cpus=4 --memory=6144

tools\minikube.exe image build -t distributed-git-storage-node:minikube -f src/DistributedGitStorage.StorageNode/Dockerfile .

tools\minikube.exe image build -t distributed-git-storage-control-plane:minikube -f src/DistributedGitStorage.Web/Dockerfile .

kubectl apply -f deploy/kubernetes/minikube.yaml
kubectl wait --for=condition=Ready pods --all -n distributed-git-storage --timeout=180s
kubectl get pods,pvc,pv -n distributed-git-storage
```

В отдельном терминале открыть доступ к Control Plane:

```powershell
kubectl port-forward `
  -n distributed-git-storage service/control-plane 5080:8080
```

## Регистрация топологии

Миграция не создаёт инфраструктурные записи. После развёртывания кластер и узлы регистрируются явно через API:

```powershell
$cluster = Invoke-RestMethod -Method Post `
  -Uri http://localhost:5080/storage-clusters `
  -ContentType application/json `
  -Body '{"name":"minikube-cluster"}'

0..2 | ForEach-Object {
  $name = "storage-node-$_"
  $address = "http://${name}.storage-node:8080"
  Invoke-RestMethod -Method Post `
    -Uri "http://localhost:5080/storage-clusters/$($cluster.id)/nodes" `
    -ContentType application/json `
    -Body (@{
      name = $name
      address = $address
      internalAddress = $address
    } | ConvertTo-Json)
}
```

После регистрации `/health/ready` должен вернуть HTTP 200.

## Проверка сохранности volume

После создания репозитория и `git push` удалить pod основной ноды:

```powershell
kubectl delete pod storage-node-0 -n distributed-git-storage
kubectl wait --for=condition=Ready pod/storage-node-0 `
  -n distributed-git-storage --timeout=120s
kubectl get pod,pvc -n distributed-git-storage
```

StatefulSet создаст `storage-node-0` заново и подключит к нему прежний PVC. SHA ветки можно проверить непосредственно на каждой ноде:

```powershell
kubectl exec -n distributed-git-storage storage-node-0 -- `
  git -C /var/lib/git/repositories/RELATIVE_PATH rev-parse refs/heads/main
```

`persistentVolumeReclaimPolicy: Retain` защищает PV при удалении PVC. Он не заменяет внешний backup: snapshots и выгрузку в S3 следует проектировать отдельно под CSI-драйвер production-кластера.
