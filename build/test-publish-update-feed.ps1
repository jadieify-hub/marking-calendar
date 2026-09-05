[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$publisher = Join-Path $PSScriptRoot 'publish-update-feed.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('marking-feed-test-' + [guid]::NewGuid().ToString('N'))
$remote = Join-Path $testRoot 'remote.git'
$checkout = Join-Path $testRoot 'checkout'
$artifacts = Join-Path $testRoot 'artifacts'
New-Item -ItemType Directory -Path $testRoot, $artifacts | Out-Null

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -cne $Actual) { throw "$Message : ожидалось '$Expected', получено '$Actual'" }
}

function Publish-Version([string]$Version) {
    $fileName = "MarkingCalendar-$Version-full.nupkg"
    $packagePath = Join-Path $artifacts $fileName
    [IO.File]::WriteAllText($packagePath, "package $Version")
    $feed = @{ Assets = @(@{
        PackageId = 'MarkingCalendar'; Version = $Version; Type = 'Full'; FileName = $fileName
        SHA256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
        SHA1 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA1).Hash
        Size = (Get-Item -LiteralPath $packagePath).Length
    }) } | ConvertTo-Json -Depth 4 -Compress
    [IO.File]::WriteAllText((Join-Path $artifacts 'releases.win.json'), $feed)
    & $publisher -RepositoryPath $checkout -ArtifactsPath $artifacts
}

try {
    git init --bare --quiet $remote
    git init --quiet --initial-branch=main $checkout
    git -C $checkout config user.name 'Feed test'
    git -C $checkout config user.email 'feed-test@example.invalid'
    [IO.File]::WriteAllText((Join-Path $checkout 'README.md'), 'main stays unchanged')
    git -C $checkout add README.md
    git -C $checkout commit --quiet -m 'initial main'
    git -C $checkout remote add origin $remote
    git -C $checkout push --quiet origin main main:refs/heads/data
    $originalHead = git -C $checkout rev-parse HEAD
    [IO.File]::WriteAllText((Join-Path $checkout 'README.md'), 'uncommitted user work')
    git -C $checkout add README.md
    $originalIndex = git -C $checkout diff --cached

    Publish-Version '0.1.9'
    Publish-Version '0.1.10'
    Publish-Version '0.1.11'
    Publish-Version '0.1.11'

    $files = (git --git-dir=$remote ls-tree --name-only releases) -join ','
    Assert-Equal 'MarkingCalendar-0.1.10-full.nupkg,MarkingCalendar-0.1.11-full.nupkg,releases.win.json' $files 'В ветке только два последних полных пакета и feed'
    Assert-Equal '1' (git --git-dir=$remote rev-list --count releases) 'История releases пересоздаётся'
    $feed = (git --git-dir=$remote show releases:releases.win.json) | ConvertFrom-Json
    Assert-Equal '0.1.11' $feed.Assets[0].Version 'Feed указывает на последний выпуск'
    Assert-Equal 'package 0.1.10' (git --git-dir=$remote show releases:MarkingCalendar-0.1.10-full.nupkg) 'Предыдущий пакет сохранён без изменений'

    $rejected = $false
    try { Publish-Version '0.1.9' } catch { $rejected = $_.Exception.Message -like '*старее*' }
    Assert-Equal $true $rejected 'Запоздалый workflow не откатывает канал обновлений'
    Assert-Equal $originalHead (git --git-dir=$remote rev-parse main) 'main не изменена'
    Assert-Equal $originalHead (git --git-dir=$remote rev-parse data) 'data не изменена'
    Assert-Equal $originalHead (git -C $checkout rev-parse HEAD) 'Рабочая ветка не переключена'
    Assert-Equal ($originalIndex -join "`n") ((git -C $checkout diff --cached) -join "`n") 'Индекс пользователя сохранён'
    Write-Host 'OK: публикация, два пакета, orphan-история, повторный запуск, защита от отката, main/data/index.'
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTestRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTestRoot) -like 'marking-feed-test-*') {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
