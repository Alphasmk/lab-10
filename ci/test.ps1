$ErrorActionPreference = 'Stop'
$primary = "riot-ci-primary-$env:BUILD_NUMBER"
$replica = "riot-ci-replica-$env:BUILD_NUMBER"
$password = [guid]::NewGuid().ToString('N')
$server = $null
try {
    foreach ($item in @(@($primary, 16454), @($replica, 16455))) {
        docker run -d --name $item[0] -e POSTGRES_USER=lab04 -e "POSTGRES_PASSWORD=$password" -e POSTGRES_DB=event_management -p "127.0.0.1:$($item[1]):5432" postgres:17
        if ($LASTEXITCODE -ne 0) { throw 'Failed to start PostgreSQL' }
    }
    foreach ($name in @($primary, $replica)) {
        $ready = $false
        for ($i = 0; $i -lt 60; $i++) {
            docker exec $name pg_isready -U lab04 -d event_management 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { $ready = $true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) { throw "PostgreSQL $name is not ready" }
        $databaseCreated = $false
        for ($i = 0; $i -lt 30; $i++) {
            cmd /c "docker exec $name createdb -U lab04 ci_server 2>nul"
            if ($LASTEXITCODE -eq 0) { $databaseCreated = $true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $databaseCreated) { throw "Failed to create server database in $name" }
    }
    $env:LAB06_TEST_DATABASE = "Host=localhost;Port=16454;Database=event_management;Username=lab04;Password=$password;Timeout=3"
    $env:ConnectionStrings__Database = "Host=localhost;Port=16454;Database=ci_server;Username=lab04;Password=$password;Timeout=3"
    $env:ConnectionStrings__Replica = "Host=localhost;Port=16455;Database=ci_server;Username=lab04;Password=$password;Timeout=3"
    $env:LAB_TEST_SERVER_URL = 'http://127.0.0.1:55000'
    $env:ASPNETCORE_URLS = $env:LAB_TEST_SERVER_URL
    New-Item -ItemType Directory -Force -Path TestResults | Out-Null
    $server = Start-Process -FilePath dotnet -ArgumentList 'Server/bin/Release/net10.0/Server.dll' -PassThru -WindowStyle Hidden -RedirectStandardOutput 'TestResults/server.log' -RedirectStandardError 'TestResults/server-error.log'
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        try { Invoke-WebRequest "$env:LAB_TEST_SERVER_URL/api/health" -UseBasicParsing | Out-Null; $ready = $true; break } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw 'Test server is not ready' }
    dotnet test EventManagement.slnx --configuration Release --no-build --no-restore --logger 'trx;LogFilePrefix=tests' --results-directory TestResults
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id }
    docker rm -f $primary $replica 2>$null | Out-Null
}
