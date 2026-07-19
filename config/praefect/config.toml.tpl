listen_addr = "0.0.0.0:2305"
prometheus_listen_addr = "0.0.0.0:9652"

[database]
host = "praefect-postgres"
port = 5432
dbname = "praefect"
user = "praefect"
password = "praefect"
sslmode = "disable"

[replication]
batch_size = 10

[reconciliation]
scheduling_interval = "10s"

[failover]
enabled = true

[[virtual_storage]]
name = "cluster"
default_replication_factor = 3

[[virtual_storage.node]]
storage = "gitaly-1"
address = "tcp://gitaly-1:8075"

[[virtual_storage.node]]
storage = "gitaly-2"
address = "tcp://gitaly-2:8075"

[[virtual_storage.node]]
storage = "gitaly-3"
address = "tcp://gitaly-3:8075"

[logging]
format = "json"
level = "info"
