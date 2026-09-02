[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$workRoot = Join-Path $repoRoot 'build\.work'
$publishPath = Join-Path $workRoot 'publish'
$velopackPath = Join-Path $workRoot 'velopack'
$artifactsPath = Join-Path $repoRoot 'artifacts'
$projectPath = Join-Path $repoRoot 'src\MarkingCalendar.App\MarkingCalendar.App.csproj'
$iconPath = Join-Path $repoRoot 'src\MarkingCalendar.App\Resources\calendar.ico'

function Assert-SafeWorkspacePath([string]$Path) {
    $resolved = [System.IO.Path]::GetFullPath($Path)
    $prefix = $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Небезопасный путь вне репозитория: $resolved"
    }
}

function Invoke-Checked([string]$Description, [scriptblock]$Action) {
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Description завершилось с кодом $LASTEXITCODE"
    }
}

function New-DeterministicZip([string]$SourceDirectory, [string]$DestinationPath) {
    Add-Type -AssemblyName System.IO.Compression
    $fileStream = [System.IO.File]::Open($DestinationPath, [System.IO.FileMode]::CreateNew)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $fileStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            $timestamp = [System.DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
            foreach ($file in Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File | Sort-Object FullName) {
                $relativePath = [System.IO.Path]::GetRelativePath($SourceDirectory, $file.FullName).Replace('\', '/')
                $entry = $archive.CreateEntry($relativePath, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $inputStream = $file.OpenRead()
                $outputStream = $entry.Open()
                try {
                    $inputStream.CopyTo($outputStream)
                }
                finally {
                    $outputStream.Dispose()
                    $inputStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $fileStream.Dispose()
    }
}

foreach ($path in @($workRoot, $publishPath, $velopackPath, $artifactsPath)) {
    Assert-SafeWorkspacePath $path
}
foreach ($path in @($workRoot, $artifactsPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
New-Item -ItemType Directory -Force -Path $publishPath, $velopackPath, $artifactsPath | Out-Null

Push-Location $repoRoot
try {
    Invoke-Checked 'Восстановление локальных инструментов' { dotnet tool restore }
    Invoke-Checked 'Восстановление зависимостей приложения' {
        dotnet restore $projectPath -r win-x64 --locked-mode
    }
    Invoke-Checked 'Публикация приложения' {
        dotnet publish $projectPath -c Release -r win-x64 --self-contained false --no-restore -o $publishPath `
            -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false
    }
    Invoke-Checked 'Упаковка Velopack' {
        dotnet tool run vpk -- pack `
            --packId MarkingCalendar `
            --packVersion $Version `
            --packDir $publishPath `
            --mainExe MarkingCalendar.exe `
            --packTitle 'Календарь маркировки' `
            --packAuthors KRS `
            --icon $iconPath `
            --runtime win-x64 `
            --framework net10-x64-desktop `
            --outputDir $velopackPath `
            --noPortable true
    }
}
finally {
    Pop-Location
}

$setupCandidates = @(Get-ChildItem -LiteralPath $velopackPath -Filter '*-Setup.exe' -File)
$packageCandidates = @(Get-ChildItem -LiteralPath $velopackPath -Filter 'MarkingCalendar-*-full.nupkg' -File)
$feedSource = Join-Path $velopackPath 'releases.win.json'
if ($setupCandidates.Count -ne 1 -or $packageCandidates.Count -ne 1 -or -not (Test-Path -LiteralPath $feedSource -PathType Leaf)) {
    throw 'Velopack не создал ожидаемые Setup, full.nupkg и releases.win.json.'
}

$setupDestination = Join-Path $artifactsPath 'MarkingCalendar-Setup.exe'
$portableDestination = Join-Path $artifactsPath 'MarkingCalendar-Portable.zip'
$feedDestination = Join-Path $artifactsPath 'releases.win.json'
$packageDestination = Join-Path $artifactsPath $packageCandidates[0].Name
Copy-Item -LiteralPath $setupCandidates[0].FullName -Destination $setupDestination
Copy-Item -LiteralPath $feedSource -Destination $feedDestination
Copy-Item -LiteralPath $packageCandidates[0].FullName -Destination $packageDestination
New-DeterministicZip $publishPath $portableDestination

$checksumFiles = @($setupDestination, $portableDestination, $feedDestination, $packageDestination) | Sort-Object
$checksumLines = foreach ($file in $checksumFiles) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToUpperInvariant()
    "$hash  $(Split-Path -Leaf $file)"
}
[System.IO.File]::WriteAllLines(
    (Join-Path $artifactsPath 'SHA256SUMS.txt'),
    $checksumLines,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Релиз $Version собран в $artifactsPath"
