#!/usr/bin/env bash
set -eu
primary="riot-ci-primary-${BUILD_NUMBER:-local}"
replica="riot-ci-replica-${BUILD_NUMBER:-local}"
password="$(cat /proc/sys/kernel/random/uuid)"
server_pid=""
cleanup() {
    if [ -n "$server_pid" ]; then kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true; fi
    docker rm -f "$primary" "$replica" >/dev/null 2>&1 || true
}
trap cleanup EXIT
docker run -d --name "$primary" -e POSTGRES_USER=lab04 -e "POSTGRES_PASSWORD=$password" -e POSTGRES_DB=event_management -p 55444:5432 postgres:17 >/dev/null
docker run -d --name "$replica" -e POSTGRES_USER=lab04 -e "POSTGRES_PASSWORD=$password" -e POSTGRES_DB=event_management -p 55445:5432 postgres:17 >/dev/null
for name in "$primary" "$replica"; do
    ready=0
    for i in $(seq 1 60); do
        if docker exec "$name" pg_isready -U lab04 -d event_management >/dev/null 2>&1; then ready=1; break; fi
        sleep 1
    done
    [ "$ready" -eq 1 ] || { echo "PostgreSQL $name не готов"; exit 1; }
done
export LAB06_TEST_DATABASE="Host=host.docker.internal;Port=55444;Database=event_management;Username=lab04;Password=$password;Timeout=3"
export ConnectionStrings__Database="$LAB06_TEST_DATABASE"
export ConnectionStrings__Replica="Host=host.docker.internal;Port=55445;Database=event_management;Username=lab04;Password=$password;Timeout=3"
export LAB_TEST_SERVER_URL=http://127.0.0.1:55000
export ASPNETCORE_URLS="$LAB_TEST_SERVER_URL"
mkdir -p TestResults
dotnet Server/bin/Release/net10.0/Server.dll >TestResults/server.log 2>&1 &
server_pid=$!
ready=0
for i in $(seq 1 60); do
    if curl -fsS "$LAB_TEST_SERVER_URL/api/health" >/dev/null 2>&1; then ready=1; break; fi
    sleep 1
done
[ "$ready" -eq 1 ] || { echo 'Тестовый сервер не готов'; cat TestResults/server.log; exit 1; }
dotnet test EventManagement.slnx --configuration Release --no-build --no-restore --logger 'trx;LogFilePrefix=tests' --results-directory TestResults
