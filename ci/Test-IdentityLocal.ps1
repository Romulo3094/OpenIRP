param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [int]$Port = 55432
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$runRoot = Join-Path $repoRoot ('artifacts\paso26-' + [Guid]::NewGuid().ToString('N'))
$clusterPath = Join-Path $runRoot 'postgres'
$pwFile = Join-Path $runRoot 'init-password.tmp'
$started = $false
$previousTest = $env:OPENIRP_TEST_POSTGRES
$previousConnection = $env:ConnectionStrings__OpenIRP
$previousPassword = $env:PGPASSWORD
$previousBootstrap = $env:OPENIRP_BOOTSTRAP_ENABLED
$previousEmail = $env:OPENIRP_BOOTSTRAP_EMAIL
$previousBootstrapPassword = $env:OPENIRP_BOOTSTRAP_PASSWORD
function CheckExit([string]$step) { if ($LASTEXITCODE -ne 0) { throw "$step failed (exit $LASTEXITCODE)" } }
if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { throw "Port $Port is already in use; existing server untouched." }
if (!(Test-Path (Join-Path $PostgresBin 'initdb.exe'))) { throw 'PostgreSQL binaries not found.' }
New-Item -ItemType Directory -Path $runRoot | Out-Null
$testPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
try {
    Set-Content -LiteralPath $pwFile -Value $testPassword -Encoding ascii
    & "$PostgresBin\initdb.exe" -D $clusterPath -U openirp_test_owner --pwfile=$pwFile --auth=scram-sha-256 --encoding=UTF8 --locale=C
    CheckExit 'initdb'
    Remove-Item -LiteralPath $pwFile
    & "$PostgresBin\pg_ctl.exe" -D $clusterPath -l (Join-Path $runRoot 'postgres.log') -o "-h 127.0.0.1 -p $Port" -w start
    CheckExit 'PostgreSQL start'
    $started = $true
    $env:PGPASSWORD = $testPassword
    $env:OPENIRP_BOOTSTRAP_ENABLED = $null
    $env:OPENIRP_TEST_POSTGRES = "Host=127.0.0.1;Port=$Port;Database=postgres;Username=openirp_test_owner;Password=$testPassword;Include Error Detail=false"
    $env:ConnectionStrings__OpenIRP = "Host=127.0.0.1;Port=$Port;Database=openirp_test_schema;Username=openirp_test_owner;Password=$testPassword"
    Push-Location $repoRoot
    try {
        & "$PostgresBin\createdb.exe" -h 127.0.0.1 -p $Port -U openirp_test_owner openirp_test_schema
        CheckExit 'create disposable schema database'
        dotnet build apps/api/OpenIRP.slnx --configuration Release
        CheckExit 'build'
        dotnet ef migrations script --idempotent --project apps/api/OpenIRP.Infrastructure --startup-project apps/api/OpenIRP.Api --configuration Release --no-build --output (Join-Path $runRoot 'migrations.sql')
        CheckExit 'migration script'
        dotnet ef database update --project apps/api/OpenIRP.Infrastructure --startup-project apps/api/OpenIRP.Api --configuration Release --no-build
        CheckExit 'migration'
        & "$PostgresBin\psql.exe" -w -h 127.0.0.1 -p $Port -U openirp_test_owner -d openirp_test_schema -v ON_ERROR_STOP=1 -f ci/verify-identity-schema.sql
        CheckExit 'existing schema checks'
        & "$PostgresBin\pg_dump.exe" -w -h 127.0.0.1 -p $Port -U openirp_test_owner -d openirp_test_schema -Fc -f (Join-Path $runRoot 'schema-only-fixtures-rolled-back.dump')
        CheckExit 'disposable schema backup'
        # The schema-check database is new and isolated; exercise the real offline command there.
        dotnet apps/api/OpenIRP.Api/bin/Release/net10.0/OpenIRP.Api.dll --bootstrap-platform-admin
        if ($LASTEXITCODE -ne 1) { throw 'Disabled bootstrap must fail closed.' }
        $env:OPENIRP_BOOTSTRAP_ENABLED = 'true'
        $env:OPENIRP_BOOTSTRAP_EMAIL = 'cli-smoke@example.test'
        $env:OPENIRP_BOOTSTRAP_PASSWORD = 'Smoke!A9' + $testPassword
        dotnet apps/api/OpenIRP.Api/bin/Release/net10.0/OpenIRP.Api.dll --bootstrap-platform-admin
        CheckExit 'CLI bootstrap'
        dotnet apps/api/OpenIRP.Api/bin/Release/net10.0/OpenIRP.Api.dll --bootstrap-platform-admin
        if ($LASTEXITCODE -ne 1) { throw 'Repeated bootstrap must be refused.' }
        $env:OPENIRP_BOOTSTRAP_ENABLED = $null
        $env:OPENIRP_BOOTSTRAP_EMAIL = $null
        $env:OPENIRP_BOOTSTRAP_PASSWORD = $null
        dotnet test apps/api/OpenIRP.IntegrationTests --configuration Release --no-build --logger 'trx;LogFileName=paso26.trx' --results-directory $runRoot
        CheckExit 'integration tests'
        dotnet ef migrations has-pending-model-changes --project apps/api/OpenIRP.Infrastructure --startup-project apps/api/OpenIRP.Api --configuration Release --no-build
        CheckExit 'model consistency'
    } finally { Pop-Location }
} finally {
    if ($started) { & "$PostgresBin\pg_ctl.exe" -D $clusterPath -m fast -w stop }
    if (Test-Path -LiteralPath $pwFile) { Remove-Item -LiteralPath $pwFile }
    $env:OPENIRP_TEST_POSTGRES = $previousTest
    $env:ConnectionStrings__OpenIRP = $previousConnection
    $env:PGPASSWORD = $previousPassword
    $env:OPENIRP_BOOTSTRAP_ENABLED = $previousBootstrap
    $env:OPENIRP_BOOTSTRAP_EMAIL = $previousEmail
    $env:OPENIRP_BOOTSTRAP_PASSWORD = $previousBootstrapPassword
    Write-Host "Local evidence: $runRoot"
}
