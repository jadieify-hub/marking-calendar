[CmdletBinding()]
param(
    [string]$ArtifactsPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedArtifacts = [System.IO.Path]::GetFullPath($ArtifactsPath)
$setupPath = Join-Path $resolvedArtifacts 'MarkingCalendar-Setup.exe'
$portablePath = Join-Path $resolvedArtifacts 'MarkingCalendar-Portable.zip'
$checksumsPath = Join-Path $resolvedArtifacts 'SHA256SUMS.txt'
$feedPath = Join-Path $resolvedArtifacts 'releases.win.json'

foreach ($requiredPath in @($setupPath, $portablePath, $checksumsPath, $feedPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Отсутствует обязательный артефакт: $requiredPath"
    }
}

$fullPackages = @(Get-ChildItem -LiteralPath $resolvedArtifacts -Filter 'MarkingCalendar-*-full.nupkg' -File)
if ($fullPackages.Count -ne 1) {
    throw "Ожидался ровно один полный пакет Velopack, найдено: $($fullPackages.Count)"
}
$fullPackagePath = $fullPackages[0].FullName

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($portablePath)
try {
    $entryNames = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($requiredEntry in @('MarkingCalendar.exe', 'wwwroot/index.html')) {
        if ($entryNames -notcontains $requiredEntry) {
            throw "Portable-архив не содержит $requiredEntry"
        }
    }
    foreach ($bundledRuntime in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationFramework.dll')) {
        if ($entryNames | Where-Object { [System.IO.Path]::GetFileName($_).Equals($bundledRuntime, [System.StringComparison]::OrdinalIgnoreCase) }) {
            throw "Portable-архив не должен включать .NET Runtime: $bundledRuntime"
        }
    }
}
finally {
    $archive.Dispose()
}

$expectedHashes = @{}
foreach ($line in Get-Content -LiteralPath $checksumsPath -Encoding utf8) {
    if ($line -notmatch '^([A-F0-9]{64})  (.+)$') {
        throw "Некорректная строка SHA256SUMS.txt: $line"
    }
    $expectedHashes[$Matches[2]] = $Matches[1]
}

foreach ($artifactPath in @($setupPath, $portablePath, $feedPath, $fullPackagePath)) {
    $fileName = Split-Path -Leaf $artifactPath
    if (-not $expectedHashes.ContainsKey($fileName)) {
        throw "В SHA256SUMS.txt отсутствует $fileName"
    }
    $actualHash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actualHash -cne $expectedHashes[$fileName]) {
        throw "Контрольная сумма не совпадает для $fileName"
    }
}

Write-Host "Пакет проверен: $resolvedArtifacts"
