bin_dir = "/usr/local/bin"
listen_addr = "0.0.0.0:8075"
runtime_dir = "/tmp"

[gitlab]
url = "http://unused"
secret_file = "/config/gitlab-shell-secret"

[[storage]]
name = "gitaly-2"
path = "/home/git/repositories"

[logging]
format = "json"
level = "info"
dir = "/var/log/gitaly"
