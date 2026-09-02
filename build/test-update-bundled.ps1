[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$refreshScript = Join-Path $PSScriptRoot 'update-bundled.ps1'
$bundledSource = Join-Path $repoRoot 'src\MarkingCalendar.App\Resources\bundled-source.json'
$workRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'build\.work'))
$testRoot = Join-Path $workRoot ('bundled-test-' + [guid]::NewGuid().ToString('N'))

try {
    $publicRoot = Join-Path $testRoot 'data'
    $historyRoot = Join-Path $publicRoot 'history'
    $outputRoot = Join-Path $testRoot 'output'
    New-Item -ItemType Directory -Path $historyRoot, $outputRoot -Force | Out-Null

    $smallPayload = Join-Path $testRoot 'small.json'
    [System.IO.File]::WriteAllText(
        $smallPayload,
        '{"data":{"items":[{"date_start":"01.09.2026"}]}}',
        [System.Text.UTF8Encoding]::new($false))

    $rejected = $false
    try {
        & $refreshScript -SourcePath $smallPayload -ValidateOnly
    }
    catch {
        $rejected = $_.Exception.Message -match '100'
    }
    if (-not $rejected) {
        throw 'Проверка должна отклонять снимок, содержащий менее 100 событий.'
    }

    & $refreshScript -SourcePath $bundledSource -ValidateOnly

    $sourcePayload = [System.IO.File]::ReadAllText($bundledSource, [System.Text.Encoding]::UTF8)
    $eventCount = @(($sourcePayload | ConvertFrom-Json).data.items).Count
    [System.IO.File]::WriteAllText((Join-Path $publicRoot 'source.json'), $sourcePayload, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $historyRoot 'changes.json'), '{"batches":[]}', [System.Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $repoRoot 'assets\groups\groups.json') -Destination (Join-Path $publicRoot 'groups.json')
    $generatedAt = [DateTimeOffset]'2026-09-01T09:00:00Z'
    $manifest = [ordered]@{
        schemaVersion = 1
        generatedAt = $generatedAt.ToString('o')
        snapshotId = 'fixture'
        eventCount = $eventCount
        batchCount = 0
        groupsUrl = 'groups.json'
        files = [ordered]@{
            source = 'source.json'
            history = 'history/changes.json'
        }
    } | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText((Join-Path $publicRoot 'manifest.json'), $manifest, [System.Text.UTF8Encoding]::new($false))

    $destination = Join-Path $outputRoot 'bundled-source.json'
    $metadata = Join-Path $outputRoot 'bundled-metadata.json'
    $history = Join-Path $outputRoot 'bundled-history.json'
    $groups = Join-Path $outputRoot 'bundled-groups.json'
    & $refreshScript `
        -FromPublic `
        -PublicDataPath $publicRoot `
        -ReferenceTime $generatedAt.AddDays(7) `
        -DestinationPath $destination `
        -MetadataPath $metadata `
        -HistoryDestinationPath $history `
        -GroupsDestinationPath $groups

    if (-not (Test-Path -LiteralPath $destination) -or -not (Test-Path -LiteralPath $history) -or -not (Test-Path -LiteralPath $groups)) {
        throw 'Режим -FromPublic не записал снимок, историю и карту групп.'
    }
    if ([System.IO.File]::ReadAllText($groups, [System.Text.Encoding]::UTF8) -ne [System.IO.File]::ReadAllText((Join-Path $publicRoot 'groups.json'), [System.Text.Encoding]::UTF8)) {
        throw 'Карта групп перенесена из публичной ветки с изменениями.'
    }
    $writtenMetadata = [System.IO.File]::ReadAllText($metadata, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    if ([DateTimeOffset]$writtenMetadata.retrievedAt -ne $generatedAt) {
        throw 'В метаданные не перенесено время публичного манифеста.'
    }

    $staleRejected = $false
    try {
        & $refreshScript `
            -FromPublic `
            -PublicDataPath $publicRoot `
            -ReferenceTime $generatedAt.AddDays(7).AddSeconds(1) `
            -ValidateOnly
    }
    catch {
        $staleRejected = $_.Exception.Message -match 'старше 7 дней'
    }
    if (-not $staleRejected) {
        throw 'Режим -FromPublic должен отклонять снимок старше 7 дней.'
    }

    Write-Host 'Проверка update-bundled.ps1 пройдена.'
}
finally {
    $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    $workPrefix = $workRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($resolvedTestRoot.StartsWith($workPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTestRoot)) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
