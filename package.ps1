$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskStage = Join-Path $taskRoot 'release\TaskBAR-0.3.0-win-x64'
$taskSource = Join-Path $taskStage 'source'
if (Test-Path -LiteralPath $taskStage) {
    $taskResolved = (Resolve-Path -LiteralPath $taskStage).Path
    $taskReleaseRoot = [System.IO.Path]::GetFullPath((Join-Path $taskRoot 'release')) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $taskResolved.StartsWith($taskReleaseRoot,[System.StringComparison]::OrdinalIgnoreCase) -or [System.IO.Path]::GetFileName($taskResolved) -ne 'TaskBAR-0.3.0-win-x64') { throw 'Pasta de staging fora do destino esperado.' }
    Remove-Item -LiteralPath $taskResolved -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $taskStage,$taskSource | Out-Null
foreach ($taskItem in @('TaskBAR.exe','TaskBAR.exe.config','ExplorerTAP.dll','LICENSE.md','LICENSE-TranslucentTB.md','README.md','THIRD_PARTY.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $taskRoot ('dist\TaskBAR-0.3.0\' + $taskItem)) -Destination $taskStage -Force }
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs') -Destination $taskStage -Recurse -Force
foreach ($taskItem in @('src','vendor','tests','docs')) { Copy-Item -LiteralPath (Join-Path $taskRoot $taskItem) -Destination $taskSource -Recurse -Force }
foreach ($taskItem in @('build.ps1','package.ps1','.gitignore','LICENSE.md','THIRD_PARTY.md','README.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $taskRoot $taskItem) -Destination $taskSource -Force }
Copy-Item -LiteralPath (Join-Path $taskRoot 'artifacts\0.3.0\self-test.txt') -Destination $taskStage -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'artifacts\0.3.0\preview.png') -Destination $taskStage -Force
foreach ($taskItem in @('tray-smoke.txt','settings-smoke.txt','profile-preview.png','runtime-diagnostics-host.json')) { if (Test-Path (Join-Path $taskRoot ('artifacts\0.3.0\' + $taskItem))) { Copy-Item (Join-Path $taskRoot ('artifacts\0.3.0\' + $taskItem)) $taskStage -Force } }
$taskZip = Join-Path $taskRoot 'release\TaskBAR-0.3.0-win-x64.zip'
Compress-Archive -LiteralPath $taskStage -DestinationPath $taskZip -Force
Get-FileHash -LiteralPath $taskZip -Algorithm SHA256 | Format-List
