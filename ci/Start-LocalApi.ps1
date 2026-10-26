param([switch]$Bootstrap, [string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$config = docker inspect openirp-postgres | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or !$config[0].State.Running) { throw 'Start the local openirp-postgres container first.' }
$binding = $config[0].NetworkSettings.Ports.'5432/tcp'
if ($binding.Count -ne 1 -or $binding[0].HostIp -ne '127.0.0.1' -or $binding[0].HostPort -ne '5433') {
    throw 'Expected development PostgreSQL bound only to 127.0.0.1:5433.'
}
$settings = @{}
foreach ($entry in $config[0].Config.Env) { $parts = $entry.Split('=', 2); $settings[$parts[0]] = $parts[1] }
if ($settings.POSTGRES_DB -ne 'openirp_dev' -or $settings.POSTGRES_USER -ne 'openirp') { throw 'Unexpected database identity.' }
$previousConnection = $env:ConnectionStrings__OpenIRP
$previousEnabled = $env:OPENIRP_BOOTSTRAP_ENABLED
$previousEmail = $env:OPENIRP_BOOTSTRAP_EMAIL
$previousPassword = $env:OPENIRP_BOOTSTRAP_PASSWORD
$previousEvidenceRoot = $env:EvidenceFiles__RootPath
$securePassword = $null
try {
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host'] = '127.0.0.1'
    $connection['Port'] = '5433'
    $connection['Database'] = 'openirp_dev'
    $connection['Username'] = 'openirp'
    $connection['Password'] = $settings.POSTGRES_PASSWORD
    $env:ConnectionStrings__OpenIRP = $connection.ConnectionString
    if (!$EvidenceRoot) { $EvidenceRoot = $previousEvidenceRoot }
    if (!$EvidenceRoot) { $EvidenceRoot = Join-Path $repoRoot ".openirp-data/evidence" }
    $env:EvidenceFiles__RootPath = [IO.Path]::GetFullPath($EvidenceRoot)
    $env:OPENIRP_BOOTSTRAP_ENABLED = $null
    if ($Bootstrap) {
        $env:OPENIRP_BOOTSTRAP_EMAIL = Read-Host 'Correo del administrador verificado fuera de banda'
        $securePassword = Read-Host 'Contraseña inicial (mínimo 12 caracteres)' -AsSecureString
        $env:OPENIRP_BOOTSTRAP_PASSWORD = [System.Net.NetworkCredential]::new('', $securePassword).Password
        $env:OPENIRP_BOOTSTRAP_ENABLED = 'true'
        dotnet run --project (Join-Path $repoRoot 'apps/api/OpenIRP.Api') --no-launch-profile -- --bootstrap-platform-admin
    } else {
        dotnet run --project (Join-Path $repoRoot 'apps/api/OpenIRP.Api') --launch-profile https
    }
    if ($LASTEXITCODE -ne 0) { throw "Local API command failed (exit $LASTEXITCODE)." }
} finally {
    $env:EvidenceFiles__RootPath = $previousEvidenceRoot
    $env:ConnectionStrings__OpenIRP = $previousConnection
    $env:OPENIRP_BOOTSTRAP_ENABLED = $previousEnabled
    $env:OPENIRP_BOOTSTRAP_EMAIL = $previousEmail
    $env:OPENIRP_BOOTSTRAP_PASSWORD = $previousPassword
    if ($securePassword) { $securePassword.Dispose() }
}
