[CmdletBinding()]
param(
    [string]$ArtifactsPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'),
    [string]$RepositoryPath = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$feedPath = Join-Path ([IO.Path]::GetFullPath($ArtifactsPath)) 'releases.win.json'
$feed = Get-Content -LiteralPath $feedPath -Raw | ConvertFrom-Json
if (@($feed.Assets).Count -ne 1 -or $feed.Assets[0].Type -ne 'Full') {
    throw 'Канал обновлений принимает ровно один полный пакет, без delta.'
}
$asset = $feed.Assets[0]
if ($asset.PackageId -cne 'MarkingCalendar' -or $asset.Version -notmatch '^\d+\.\d+\.\d+$' -or
    $asset.FileName -cne "MarkingCalendar-$($asset.Version)-full.nupkg") {
    throw 'Ожидался полный пакет стабильной версии MarkingCalendar.'
}
$packagePath = Join-Path (Split-Path -Parent $feedPath) $asset.FileName
if ((Get-Item -LiteralPath $packagePath).Length -ne $asset.Size -or
    (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -ine $asset.SHA256) {
    throw 'Размер или SHA256 пакета не совпадает с releases.win.json.'
}

Push-Location -LiteralPath $RepositoryPath
try {
    # Рабочую ветку и индекс не трогаем: новый корневой коммит собирается из blob-объектов.
    $previousCommit = ''
    $previousPackages = @()
    $remoteRef = git ls-remote origin refs/heads/releases
    if ($remoteRef) {
        git fetch --quiet --no-tags --depth=1 origin refs/heads/releases
        $previousCommit = git rev-parse FETCH_HEAD
        $previousPackages = @(git ls-tree $previousCommit | ForEach-Object {
            if ($_ -match '^100644 blob ([a-f0-9]+)\t(MarkingCalendar-(\d+\.\d+\.\d+)-full\.nupkg)$') {
                [pscustomobject]@{ Blob = $Matches[1]; Name = $Matches[2]; Version = [version]$Matches[3]; TreeLine = $_ }
            }
        })
    }
    if ($previousPackages | Where-Object { $_.Version -gt [version]$asset.Version }) {
        throw "Версия $($asset.Version) старее уже опубликованной: канал обновлений не откатываем."
    }

    $feedBlob = git hash-object -w --no-filters -- $feedPath
    $packageBlob = git hash-object -w --no-filters -- $packagePath
    $sameVersion = $previousPackages | Where-Object Name -CEQ $asset.FileName
    if ($sameVersion -and $sameVersion.Blob -cne $packageBlob) {
        throw 'Пакет с этой версией уже опубликован с другим содержимым. Выпустите новую версию.'
    }
    $retained = @($previousPackages | Where-Object Name -CNE $asset.FileName |
        Sort-Object Version -Descending | Select-Object -First 1)
    $treeLines = @("100644 blob $feedBlob`treleases.win.json", "100644 blob $packageBlob`t$($asset.FileName)")
    $treeLines += @($retained | ForEach-Object TreeLine)
    # PowerShell добавляет CRLF в pipe; mktree считает CR частью имени файла.
    $treeProcess = [Diagnostics.Process]::new()
    $treeProcess.StartInfo = [Diagnostics.ProcessStartInfo]@{
        FileName = 'git'; Arguments = 'mktree'; WorkingDirectory = (Get-Location).Path
        UseShellExecute = $false; CreateNoWindow = $true
        RedirectStandardInput = $true; RedirectStandardOutput = $true
    }
    try {
        [void]$treeProcess.Start()
        $treeProcess.StandardInput.Write(($treeLines -join "`n") + "`n")
        $treeProcess.StandardInput.Close()
        $tree = $treeProcess.StandardOutput.ReadToEnd().Trim()
        $treeProcess.WaitForExit()
        if ($treeProcess.ExitCode -ne 0) { throw 'Не удалось собрать дерево releases.' }
    }
    finally { $treeProcess.Dispose() }
    $commit = git -c 'user.name=github-actions[bot]' -c 'user.email=41898282+github-actions[bot]@users.noreply.github.com' `
        commit-tree $tree -m "Update feed $($asset.Version)"

    # Только служебная releases: без родителей и с защитой от параллельной публикации.
    git push origin "${commit}:refs/heads/releases" "--force-with-lease=refs/heads/releases:$previousCommit"
    Write-Host "Канал обновлений $($asset.Version): $($retained.Count + 1) полных пакета, один корневой коммит."
}
finally {
    Pop-Location
}
