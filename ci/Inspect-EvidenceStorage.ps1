[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RootPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(1,100000)][int]$MaxEntries = 10000
)
$ErrorActionPreference = 'Stop'
# Offline local operator diagnostic. Reads the explicitly named private store and local development database.
# No repair, delete, move, quarantine release, or automatic purge is performed.
if(-not [IO.Path]::IsPathFullyQualified($RootPath) -or $RootPath.StartsWith('\\')) { throw 'Use an absolute local private storage root' }
$resolvedRoot = [IO.Path]::GetFullPath($RootPath)
if(-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) { throw 'Storage root does not exist' }
for($ancestor = [IO.DirectoryInfo]::new($resolvedRoot); $null -ne $ancestor; $ancestor = $ancestor.Parent) {
    if($ancestor.Exists -and (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'Refusing storage root with reparse points' }
}
if(-not [IO.Path]::IsPathFullyQualified($OutputPath)) { throw 'Use an absolute output artifact path' }
$reportPath = [IO.Path]::GetFullPath($OutputPath)
if($reportPath.StartsWith($resolvedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Report must be outside private storage' }
if(Test-Path -LiteralPath $reportPath) { throw 'Refusing to overwrite an existing report' }
$context = & docker context inspect --format '{{json .Endpoints.docker.Host}}'
if($LASTEXITCODE -ne 0 -or $context -notmatch '^("npipe:|"unix:)') { throw 'Only a local Docker endpoint is allowed' }
$binding = & docker inspect --format '{{json .NetworkSettings.Ports}}' openirp-postgres
if($LASTEXITCODE -ne 0) { throw 'Local PostgreSQL container is unavailable' }
$ports = $binding | ConvertFrom-Json
if(-not ($ports.'5432/tcp' | Where-Object { $_.HostIp -eq '127.0.0.1' -and $_.HostPort -eq '5433' })) { throw 'Unexpected PostgreSQL port binding' }
$queryLimit = $MaxEntries + 1
$rows = & docker exec openirp-postgres psql -U openirp -d openirp_dev -At -F '|' -v ON_ERROR_STOP=1 -c "SELECT storage_id,state FROM app.evidence_files ORDER BY storage_id LIMIT $queryLimit"
if($LASTEXITCODE -ne 0) { throw 'Storage metadata read failed' }
if(@($rows).Count -gt $MaxEntries) { throw 'Registered metadata limit exceeded' }
$known = @{}
foreach($row in $rows) { $parts = $row.Split('|'); $id = [guid]::Parse($parts[0]).ToString('N'); $known[$id] = $parts[1] }
$counts = [ordered]@{ registeredFiles=$known.Count; objectsKnown=0; objectsUnregistered=0; stagingKnown=0; stagingUnregistered=0; unsafeEntries=0; unrecognizedEntries=0; missingAvailableObjects=0; scannedEntries=0 }
$objectIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($area in @('objects','staging')) {
    $directory = Join-Path $resolvedRoot $area
    if(-not (Test-Path -LiteralPath $directory)) { continue }
    $info = Get-Item -LiteralPath $directory -Force
    if(-not $info.PSIsContainer -or (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'Unsafe store subdirectory' }
    foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
        $counts.scannedEntries++
        if($counts.scannedEntries -gt $MaxEntries) { throw 'Diagnostic limit exceeded; no partial success report emitted' }
        $item = Get-Item -LiteralPath $entry -Force
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $counts.unsafeEntries++; continue }
        $pattern = if($area -eq 'objects') { '^([0-9a-fA-F]{32})\.bin$' } else { '^([0-9a-fA-F]{32})\.pending$' }
        if($item.PSIsContainer -or $item.Name -notmatch $pattern) { $counts.unrecognizedEntries++; continue }
        $id = $Matches[1].ToLowerInvariant()
        if($area -eq 'objects') { [void]$objectIds.Add($id) }
        $key = $area + $(if($known.ContainsKey($id)) { 'Known' } else { 'Unregistered' })
        $counts[$key]++
    }
}
foreach($id in $known.Keys) { if($known[$id] -eq 'AVAILABLE' -and -not $objectIds.Contains($id)) { $counts.missingAvailableObjects++ } }
$result = [ordered]@{ checkedAtUtc=[DateTimeOffset]::UtcNow.ToString('O'); database='openirp_dev'; diagnosisOnly=$true; retentionPolicy='RETAIN_NO_AUTOMATIC_PURGE'; physicalIntegrityVerified=$false; malwareScanned=$false; counts=$counts }
# CreateNew prevents races from overwriting an existing artifact. Parent must already exist.
$bytes = [Text.UTF8Encoding]::new($false).GetBytes(($result | ConvertTo-Json -Depth 5))
$stream = [IO.FileStream]::new($reportPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
Write-Output "Read-only storage diagnosis saved: $reportPath"
