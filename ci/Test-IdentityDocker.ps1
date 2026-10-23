param([int]$Port = 55433)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$runRoot = Join-Path $repoRoot ('artifacts\paso26-pg17-' + [Guid]::NewGuid().ToString('N'))
$containerName = 'openirp-paso26-test-' + [Guid]::NewGuid().ToString('N')
$previousPassword = $env:POSTGRES_PASSWORD
$previousConnection = $env:ConnectionStrings__OpenIRP
$previousTest = $env:OPENIRP_TEST_POSTGRES
$containerId = $null
function CheckExit([string]$step) { if ($LASTEXITCODE -ne 0) { throw "$step failed (exit $LASTEXITCODE)" } }
if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { throw "Port $Port is occupied; existing server untouched." }
New-Item -ItemType Directory -Path $runRoot | Out-Null
try {
    $env:POSTGRES_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $containerId = docker run --rm -d --name $containerName --label openirp.paso26.test=true -p "127.0.0.1:${Port}:5432" -e POSTGRES_USER=openirp_test_owner -e POSTGRES_DB=openirp_test_schema -e POSTGRES_PASSWORD postgres:17
    CheckExit 'temporary PostgreSQL 17 container'
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        docker exec $containerId pg_isready -U openirp_test_owner -d openirp_test_schema 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (!$ready) { throw 'Temporary PostgreSQL 17 readiness failed.' }
    $env:ConnectionStrings__OpenIRP = "Host=127.0.0.1;Port=$Port;Database=openirp_test_schema;Username=openirp_test_owner;Password=$env:POSTGRES_PASSWORD"
    $env:OPENIRP_TEST_POSTGRES = "Host=127.0.0.1;Port=$Port;Database=postgres;Username=openirp_test_owner;Password=$env:POSTGRES_PASSWORD"
    Push-Location $repoRoot
    try {
        dotnet build apps/api/OpenIRP.slnx --configuration Release
        CheckExit 'build'
        dotnet ef database update --project apps/api/OpenIRP.Infrastructure --startup-project apps/api/OpenIRP.Api --configuration Release --no-build
        CheckExit 'migration'
        docker cp ci/verify-identity-schema.sql "${containerId}:/tmp/verify-identity-schema.sql"
        CheckExit 'SQL checks copy'
        docker exec $containerId psql -U openirp_test_owner -d openirp_test_schema -v ON_ERROR_STOP=1 -f /tmp/verify-identity-schema.sql
        CheckExit 'existing schema checks'
        dotnet test apps/api/OpenIRP.IntegrationTests --configuration Release --no-build --logger 'trx;LogFileName=paso26-pg17.trx' --results-directory $runRoot
        CheckExit 'PostgreSQL 17 integration tests'
        dotnet ef migrations has-pending-model-changes --project apps/api/OpenIRP.Infrastructure --startup-project apps/api/OpenIRP.Api --configuration Release --no-build
        CheckExit 'model consistency'
    } finally { Pop-Location }
} finally {
    if ($containerId) {
        $label = docker inspect --format '{{index .Config.Labels "openirp.paso26.test"}}' $containerId
        if ($label -eq 'true') { docker stop --timeout 5 $containerId }
    }
    $env:POSTGRES_PASSWORD = $previousPassword
    $env:ConnectionStrings__OpenIRP = $previousConnection
    $env:OPENIRP_TEST_POSTGRES = $previousTest
    Write-Host "PostgreSQL 17 evidence: $runRoot"
}
