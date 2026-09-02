[CmdletBinding()]
param(
    [string]$SourcePath,
    [uri]$SourceUrl = 'https://xn--80ajghhoc2aj1c8b.xn--p1ai/bitrix/services/main/ajax.php?mode=class&c=dev%3AmarkingCalendar&action=getSheduleList',
    [string]$DestinationPath,
    [string]$MetadataPath,
    [string]$HistoryDestinationPath,
    [string]$GroupsDestinationPath,
    [switch]$FromPublic,
    [string]$PublicDataPath,
    [uri]$PublicManifestUrl = 'https://raw.githubusercontent.com/jadieify-hub/marking-calendar/data/manifest.json',
    [DateTimeOffset]$RetrievedAt = [DateTimeOffset]::UtcNow,
    [DateTimeOffset]$ReferenceTime = [DateTimeOffset]::UtcNow,
    [ValidateRange(1, 1000000)]
    [int]$MinimumItemCount = 100,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
    $DestinationPath = Join-Path $repoRoot 'src\MarkingCalendar.App\Resources\bundled-source.json'
}
if ([string]::IsNullOrWhiteSpace($MetadataPath)) {
    $MetadataPath = Join-Path $repoRoot 'src\MarkingCalendar.App\Resources\bundled-metadata.json'
}
if ([string]::IsNullOrWhiteSpace($HistoryDestinationPath)) {
    $HistoryDestinationPath = Join-Path $repoRoot 'src\MarkingCalendar.App\Resources\bundled-history.json'
}
if ([string]::IsNullOrWhiteSpace($GroupsDestinationPath)) {
    $GroupsDestinationPath = Join-Path $repoRoot 'src\MarkingCalendar.App\Resources\bundled-groups.json'
}

function Assert-RepositoryPath([string]$Path) {
    $resolved = [System.IO.Path]::GetFullPath($Path)
    $prefix = $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Путь назначения находится вне репозитория: $resolved"
    }
    return $resolved
}

function Assert-PublicUri([uri]$Uri) {
    if (-not $Uri.IsAbsoluteUri -or
        $Uri.Scheme -ne 'https' -or
        $Uri.IdnHost -ne 'raw.githubusercontent.com' -or
        -not $Uri.AbsolutePath.StartsWith('/jadieify-hub/marking-calendar/data/', [System.StringComparison]::Ordinal)) {
        throw "Разрешены только публичные данные проекта из ветки data: $Uri"
    }
    return $Uri
}

function Resolve-PublicFile([string]$RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        throw 'Манифест содержит пустой путь к публичному файлу.'
    }

    if (-not [string]::IsNullOrWhiteSpace($PublicDataPath)) {
        $root = [System.IO.Path]::GetFullPath($PublicDataPath)
        $prefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        $candidate = [System.IO.Path]::GetFullPath((Join-Path $root $RelativePath))
        if (-not $candidate.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Путь из манифеста выходит за пределы каталога публичных данных: $RelativePath"
        }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Публичный файл не найден: $candidate"
        }
        return [System.IO.File]::ReadAllText($candidate, [System.Text.Encoding]::UTF8)
    }

    $uri = Assert-PublicUri ([uri]::new($PublicManifestUrl, $RelativePath))
    return [string](Invoke-WebRequest -Uri $uri -Headers @{ 'User-Agent' = 'MarkingCalendar-BundledRefresh/1.0' } -UseBasicParsing).Content
}

$historyJson = $null
$groupsJson = $null
if ($FromPublic) {
    if (-not [string]::IsNullOrWhiteSpace($SourcePath)) {
        throw 'Параметры -FromPublic и -SourcePath нельзя использовать одновременно.'
    }

    if (-not [string]::IsNullOrWhiteSpace($PublicDataPath)) {
        $resolvedPublicData = [System.IO.Path]::GetFullPath($PublicDataPath)
        $manifestPath = Join-Path $resolvedPublicData 'manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Публичный манифест не найден: $manifestPath"
        }
        $manifestJson = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
    }
    else {
        $resolvedManifestUri = Assert-PublicUri $PublicManifestUrl
        $manifestJson = [string](Invoke-WebRequest -Uri $resolvedManifestUri -Headers @{ 'User-Agent' = 'MarkingCalendar-BundledRefresh/1.0' } -UseBasicParsing).Content
    }

    try {
        $manifest = $manifestJson | ConvertFrom-Json
        $generatedAt = [DateTimeOffset]::Parse(
            [string]$manifest.generatedAt,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind)
    }
    catch {
        throw "Публичный манифест повреждён: $($_.Exception.Message)"
    }
    if ([int]$manifest.schemaVersion -ne 1) {
        throw "Версия схемы публичного манифеста не поддерживается: $($manifest.schemaVersion)."
    }
    $age = $ReferenceTime.ToUniversalTime() - $generatedAt.ToUniversalTime()
    if ($age -gt [TimeSpan]::FromDays(7)) {
        throw "Публичный снимок старше 7 дней: $($generatedAt.ToString('o'))."
    }
    if ($age -lt [TimeSpan]::FromDays(-1)) {
        throw "Время публичного снимка находится в будущем: $($generatedAt.ToString('o'))."
    }

    $json = Resolve-PublicFile ([string]$manifest.files.source)
    $historyJson = Resolve-PublicFile ([string]$manifest.files.history)
    $groupsJson = Resolve-PublicFile ([string]$manifest.groupsUrl)
    try {
        $historyPayload = $historyJson | ConvertFrom-Json
    }
    catch {
        throw "Публичная история содержит повреждённый JSON: $($_.Exception.Message)"
    }
    if ($null -eq $historyPayload.batches -or @($historyPayload.batches).Count -ne [int]$manifest.batchCount) {
        throw 'Число пакетов публичной истории не совпадает с манифестом.'
    }
    $RetrievedAt = $generatedAt
}
elseif ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $response = Invoke-WebRequest -Uri $SourceUrl -Headers @{ 'User-Agent' = 'MarkingCalendar-BundledRefresh/1.0' } -UseBasicParsing
    $json = [string]$response.Content
}
else {
    $resolvedSource = [System.IO.Path]::GetFullPath($SourcePath)
    if (-not (Test-Path -LiteralPath $resolvedSource -PathType Leaf)) {
        throw "Исходный JSON не найден: $resolvedSource"
    }
    $json = [System.IO.File]::ReadAllText($resolvedSource, [System.Text.Encoding]::UTF8)
}

if (-not $FromPublic) {
    $groupsSource = Join-Path $repoRoot 'assets\groups\groups.json'
    if (-not (Test-Path -LiteralPath $groupsSource -PathType Leaf)) {
        throw "Карта товарных групп не найдена: $groupsSource"
    }
    $groupsJson = [System.IO.File]::ReadAllText($groupsSource, [System.Text.Encoding]::UTF8)
}

try {
    $groupsPayload = $groupsJson | ConvertFrom-Json
}
catch {
    throw "Карта товарных групп содержит повреждённый JSON: $($_.Exception.Message)"
}
if ([int]$groupsPayload.schemaVersion -ne 2) {
    throw "Версия схемы карты товарных групп не поддерживается: $($groupsPayload.schemaVersion)."
}

try {
    $payload = $json | ConvertFrom-Json
}
catch {
    throw "Исходный снимок содержит повреждённый JSON: $($_.Exception.Message)"
}

$items = @($payload.data.items)
if ($null -eq $payload.data -or $null -eq $payload.data.items) {
    throw 'В исходном снимке отсутствует data.items.'
}
if ($items.Count -lt $MinimumItemCount) {
    throw "Исходный снимок содержит $($items.Count) событий; требуется не менее $MinimumItemCount."
}
if ($FromPublic -and $items.Count -ne [int]$manifest.eventCount) {
    throw 'Число событий публичного снимка не совпадает с манифестом.'
}

Write-Host "Снимок проверен: $($items.Count) событий."
if ($ValidateOnly) {
    return
}

$resolvedDestination = Assert-RepositoryPath $DestinationPath
$resolvedMetadata = Assert-RepositoryPath $MetadataPath
$resolvedHistoryDestination = if ($FromPublic) { Assert-RepositoryPath $HistoryDestinationPath } else { $null }
$resolvedGroupsDestination = Assert-RepositoryPath $GroupsDestinationPath
$directories = @(
    (Split-Path -Parent $resolvedDestination),
    (Split-Path -Parent $resolvedMetadata),
    (Split-Path -Parent $resolvedGroupsDestination)
)
if ($null -ne $resolvedHistoryDestination) {
    $directories += Split-Path -Parent $resolvedHistoryDestination
}
New-Item -ItemType Directory -Force -Path $directories | Out-Null

$metadata = [ordered]@{
    retrievedAt = $RetrievedAt.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
    sourceUrl = $SourceUrl.AbsoluteUri
    itemCount = $items.Count
} | ConvertTo-Json

$suffix = [guid]::NewGuid().ToString('N')
$writes = @(
    [pscustomobject]@{ Path = $resolvedDestination; Content = $json },
    [pscustomobject]@{ Path = $resolvedMetadata; Content = $metadata + [Environment]::NewLine },
    [pscustomobject]@{ Path = $resolvedGroupsDestination; Content = $groupsJson }
)
if ($null -ne $resolvedHistoryDestination) {
    $writes += [pscustomobject]@{ Path = $resolvedHistoryDestination; Content = $historyJson }
}

$temporaryPaths = @()
try {
    foreach ($write in $writes) {
        $temporaryPath = "$($write.Path).$suffix.tmp"
        $temporaryPaths += $temporaryPath
        [System.IO.File]::WriteAllText($temporaryPath, $write.Content, [System.Text.UTF8Encoding]::new($false))
    }
    for ($index = 0; $index -lt $writes.Count; $index++) {
        [System.IO.File]::Move($temporaryPaths[$index], $writes[$index].Path, $true)
    }
}
finally {
    foreach ($temporaryPath in $temporaryPaths) {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

if ($FromPublic) {
    Write-Host 'Встроенный снимок, метаданные, история и карта групп обновлены из публичной ветки data.'
}
else {
    Write-Host 'Встроенный снимок, метаданные и карта групп обновлены.'
}
