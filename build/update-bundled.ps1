[CmdletBinding()]
param(
    [string]$SourcePath,
    [uri]$SourceUrl = 'https://xn--80ajghhoc2aj1c8b.xn--p1ai/bitrix/services/main/ajax.php?mode=class&c=dev%3AmarkingCalendar&action=getSheduleList',
    [string]$DestinationPath,
    [string]$MetadataPath,
    [string]$HistoryDestinationPath,
    [string]$GroupsDestinationPath,
    [string]$ProductsDestinationPath,
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
if ([string]::IsNullOrWhiteSpace($ProductsDestinationPath)) {
    $ProductsDestinationPath = Join-Path $repoRoot 'src\MarkingCalendar.App\Resources\bundled-products.json'
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

function Read-PublicProducts {
    if (-not [string]::IsNullOrWhiteSpace($PublicDataPath)) {
        $productsPath = Join-Path ([System.IO.Path]::GetFullPath($PublicDataPath)) 'products.json'
        if (-not (Test-Path -LiteralPath $productsPath -PathType Leaf)) {
            return $null
        }
        return [System.IO.File]::ReadAllText($productsPath, [System.Text.Encoding]::UTF8)
    }

    $productsUri = Assert-PublicUri ([uri]'https://raw.githubusercontent.com/jadieify-hub/marking-calendar/data/products.json')
    try {
        return [string](Invoke-WebRequest -Uri $productsUri -Headers @{ 'User-Agent' = 'MarkingCalendar-BundledRefresh/1.0' } -UseBasicParsing).Content
    }
    catch {
        if ($null -ne $_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) {
            return $null
        }
        Write-Warning "Не удалось получить товарный справочник; сохранена текущая встроенная версия: $($_.Exception.Message)"
        return $null
    }
}

function Test-ProductsPayload([string]$Content) {
    try {
        $productsPayload = $Content | ConvertFrom-Json
        if ([int]$productsPayload.schemaVersion -ne 2 -or
            [string]::IsNullOrWhiteSpace([string]$productsPayload.revision) -or
            $productsPayload.revision -isnot [string] -or
            $null -eq $productsPayload.groups -or $productsPayload.groups -isnot [System.Array] -or
            @($productsPayload.groups).Count -eq 0) {
            throw 'ожидались schemaVersion 2, revision и непустой groups'
        }
        $groupIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($group in @($productsPayload.groups)) {
            foreach ($property in @('id', 'name', 'sourceUrl', 'sourceHeading', 'sourceHash', 'revision')) {
                if ($group.PSObject.Properties.Name -notcontains $property -or
                    $group.$property -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$group.$property)) {
                    throw "в группе отсутствует обязательное поле $property"
                }
            }
            foreach ($property in @('changedAt', 'checkedAt')) {
                if ($group.PSObject.Properties.Name -notcontains $property -or $null -eq $group.$property) {
                    throw "в группе отсутствует обязательное поле $property"
                }
            }
            foreach ($property in @('conditions', 'examples', 'rows', 'scope')) {
                if ($group.PSObject.Properties.Name -notcontains $property -or $null -eq $group.$property) {
                    throw "в группе отсутствует обязательное поле $property"
                }
            }
            if ($group.conditions -isnot [string] -or $group.examples -isnot [System.Array] -or $group.rows -isnot [System.Array]) {
                throw "conditions, examples или rows группы $($group.id) имеют неверный тип"
            }
            if (-not $groupIds.Add($group.id) -or @($group.examples | Where-Object { $_ -isnot [string] }).Count -gt 0) {
                throw "повторяющийся id или некорректные примеры группы $($group.id)"
            }
            if (@($group.rows).Count -eq 0) {
                throw "группа $($group.id) не содержит строк"
            }
            $groupUri = [uri]$group.sourceUrl
            if ($group.PSObject.Properties.Name -contains 'scope' -and $null -ne $group.scope) {
                $scope = $group.scope
                if ($scope.PSObject.Properties.Name -notcontains 'names' -or $scope.names -isnot [System.Array] -or
                    $scope.PSObject.Properties.Name -notcontains 'description' -or $scope.description -isnot [string] -or
                    $scope.PSObject.Properties.Name -notcontains 'sourceUrl' -or $scope.sourceUrl -isnot [string] -or
                    @($scope.names | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) }).Count -gt 0 -or
                    (@($scope.names).Count -eq 0 -and [string]::IsNullOrWhiteSpace($scope.description))) {
                    throw "некорректное содержание группы $($group.id)"
                }
                $scopeUri = [uri]$scope.sourceUrl
                if (-not $scopeUri.IsAbsoluteUri -or $scopeUri.Scheme -ne 'https' -or
                    $scopeUri.IdnHost -ne 'xn--80ajghhoc2aj1c8b.xn--p1ai' -or
                    -not $scopeUri.AbsolutePath.StartsWith('/business/projects/', [System.StringComparison]::Ordinal)) {
                    throw "источник содержания группы $($group.id) не ведёт на Честный знак"
                }
            }
            if ($groupUri.Scheme -ne 'https' -or
                $groupUri.IdnHost -ne 'xn--80ajghhoc2aj1c8b.xn--p1ai' -or
                -not $groupUri.AbsolutePath.StartsWith('/business/projects/', [System.StringComparison]::Ordinal)) {
                throw "sourceUrl группы $($group.id) не ведёт на официальный сайт Честного знака"
            }
            try {
                $changedAt = [DateTimeOffset]$group.changedAt
                $checkedAt = [DateTimeOffset]$group.checkedAt
            }
            catch {
                throw "в группе $($group.id) указаны некорректные даты"
            }
            if ($changedAt -gt $checkedAt) {
                throw "в группе $($group.id) указаны некорректные даты"
            }
            foreach ($row in @($group.rows)) {
                foreach ($property in @('section', 'sourceName', 'tnvedText', 'okpd2Text', 'conditions', 'meanings')) {
                    if ($row.PSObject.Properties.Name -notcontains $property -or $null -eq $row.$property) {
                        throw "в строке группы $($group.id) отсутствует обязательное поле $property"
                    }
                }
                foreach ($property in @('section', 'sourceName', 'tnvedText', 'okpd2Text', 'conditions')) {
                    if ($row.$property -isnot [string]) {
                        throw "поле $property строки группы $($group.id) должно быть строкой"
                    }
                }
                if ($row.meanings -isnot [System.Array]) {
                    throw "meanings строки группы $($group.id) должен быть массивом"
                }
                if ([string]::IsNullOrWhiteSpace([string]$row.sourceName) -or
                    ([string]::IsNullOrWhiteSpace([string]$row.tnvedText) -and [string]::IsNullOrWhiteSpace([string]$row.okpd2Text))) {
                    throw "строка группы $($group.id) не содержит названия или кода"
                }
                foreach ($meaning in @($row.meanings)) {
                    foreach ($property in @('code', 'name', 'sourceUrl', 'sourceContext')) {
                        if ($meaning.PSObject.Properties.Name -notcontains $property -or
                            $meaning.$property -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$meaning.$property)) {
                            throw "в пояснении кода отсутствует обязательное поле $property"
                        }
                    }
                    if ($meaning.PSObject.Properties.Name -notcontains 'searchTerms' -or
                        $null -eq $meaning.searchTerms -or $meaning.searchTerms -isnot [System.Array]) {
                        throw "в пояснении кода $($meaning.code) отсутствует searchTerms"
                    }
                    if (@($meaning.searchTerms | Where-Object { $_ -isnot [string] }).Count -gt 0) {
                        throw "searchTerms кода $($meaning.code) содержит нестроковое значение"
                    }
                    $meaningUri = [uri]$meaning.sourceUrl
                    if ($meaningUri.Scheme -ne 'https' -or $meaningUri.IdnHost -ne 'eec.eaeunion.org') {
                        throw "sourceUrl кода $($meaning.code) не ведёт на официальный сайт ЕЭК"
                    }
                }
            }
        }
        return $true
    }
    catch {
        Write-Warning "Публичный товарный справочник повреждён; сохранена текущая встроенная версия: $($_.Exception.Message)"
        return $false
    }
}

$historyJson = $null
$groupsJson = $null
$productsJson = $null
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
    $candidateProductsJson = Read-PublicProducts
    if ($null -ne $candidateProductsJson -and (Test-ProductsPayload $candidateProductsJson)) {
        $productsJson = $candidateProductsJson
    }
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
$resolvedProductsDestination = if ($null -ne $productsJson) { Assert-RepositoryPath $ProductsDestinationPath } else { $null }
$directories = @(
    (Split-Path -Parent $resolvedDestination),
    (Split-Path -Parent $resolvedMetadata),
    (Split-Path -Parent $resolvedGroupsDestination)
)
if ($null -ne $resolvedHistoryDestination) {
    $directories += Split-Path -Parent $resolvedHistoryDestination
}
if ($null -ne $resolvedProductsDestination) {
    $directories += Split-Path -Parent $resolvedProductsDestination
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
if ($null -ne $resolvedProductsDestination) {
    $writes += [pscustomobject]@{ Path = $resolvedProductsDestination; Content = $productsJson }
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
    $productsMessage = if ($null -ne $productsJson) { ' и товарный справочник' } else { '; товарный справочник сохранён без изменений' }
    Write-Host "Встроенный снимок, метаданные, история и карта групп обновлены из публичной ветки data$productsMessage."
}
else {
    Write-Host 'Встроенный снимок, метаданные и карта групп обновлены.'
}
