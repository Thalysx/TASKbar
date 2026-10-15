param([switch]$SkipChecks, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskOut = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot 'dist\TaskBAR-0.3.0' }
$taskArtifacts = Join-Path $taskRoot 'artifacts\0.3.0'
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Force -Path $taskOut,(Join-Path $taskOut 'data'),$taskArtifacts | Out-Null
Add-Type -AssemblyName System.Drawing
$taskBitmap = New-Object System.Drawing.Bitmap 64,64
$taskGraphics = [System.Drawing.Graphics]::FromImage($taskBitmap)
$taskGraphics.Clear([System.Drawing.Color]::FromArgb(16,21,30))
$taskBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(114,228,210))
$taskGraphics.FillRectangle($taskBrush,12,14,40,8)
$taskGraphics.FillRectangle($taskBrush,28,22,8,24)
$taskGraphics.FillRectangle($taskBrush,12,50,40,5)
$taskHandle = $taskBitmap.GetHicon()
$taskIcon = [System.Drawing.Icon]::FromHandle($taskHandle)
$taskIconPath = Join-Path $taskRoot 'artifacts\TaskBAR.ico'
$taskStream = [System.IO.File]::Create($taskIconPath)
$taskIcon.Save($taskStream); $taskStream.Dispose(); $taskIcon.Dispose(); $taskGraphics.Dispose(); $taskBrush.Dispose(); $taskBitmap.Dispose()
$taskArgs = @('/nologo','/target:winexe','/platform:x64','/langversion:5','/optimize+',('/out:' + (Join-Path $taskOut 'TaskBAR.exe')),('/win32manifest:' + (Join-Path $taskRoot 'src\app.manifest')),('/win32icon:' + $taskIconPath),('/resource:' + (Join-Path $taskRoot 'src\MainWindow.xaml') + ',TaskBAR.MainWindow.xaml'),('/resource:' + (Join-Path $taskRoot 'tests\fixtures\translucenttb-settings.json') + ',TaskBAR.ImportFixture.json'))
$taskArgs += '/resource:' + (Join-Path $taskRoot 'src\RulesPage.xaml') + ',TaskBAR.RulesPage.xaml'
foreach ($taskDll in @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll')) { $taskArgs += '/reference:' + (Join-Path $taskFramework $taskDll) }
foreach ($taskDll in @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll')) { $taskArgs += '/reference:' + (Join-Path $taskFramework ('WPF\' + $taskDll)) }
$taskArgs += @(Get-ChildItem (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& (Join-Path $taskFramework 'csc.exe') @taskArgs
if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'src\TaskBAR.exe.config') -Destination $taskOut
Copy-Item -LiteralPath (Join-Path $taskRoot 'vendor\ExplorerTAP.dll') -Destination $taskOut
Copy-Item -LiteralPath (Join-Path $taskRoot 'vendor\LICENSE-TranslucentTB.md') -Destination (Join-Path $taskOut 'LICENSE-TranslucentTB.md')
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE.md') -Destination $taskOut
if (Test-Path (Join-Path $taskRoot 'THIRD_PARTY.md')) { Copy-Item (Join-Path $taskRoot 'THIRD_PARTY.md') $taskOut }
if (Test-Path (Join-Path $taskRoot 'README.md')) { Copy-Item (Join-Path $taskRoot 'README.md') $taskOut }
if (Test-Path (Join-Path $taskRoot 'CHANGELOG.md')) { Copy-Item (Join-Path $taskRoot 'CHANGELOG.md') $taskOut }
if (Test-Path (Join-Path $taskRoot 'docs')) { Copy-Item (Join-Path $taskRoot 'docs') $taskOut -Recurse -Force }
if (-not $SkipChecks) {
    $taskTestPath=Join-Path $taskArtifacts 'self-test.txt'
    $taskProcess=Start-Process -FilePath (Join-Path $taskOut 'TaskBAR.exe') -ArgumentList @('--self-test',('"' + $taskTestPath + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($taskProcess.ExitCode -ne 0) { Get-Content $taskTestPath; throw 'Os testes falharam.' }
    Get-Content $taskTestPath | Select-Object -Last 1
    $taskPreviewPath=Join-Path $taskArtifacts 'preview.png'
    $taskProcess=Start-Process -FilePath (Join-Path $taskOut 'TaskBAR.exe') -ArgumentList @('--tray-preview',('"' + $taskPreviewPath + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($taskProcess.ExitCode -ne 0) { throw 'A renderização da interface falhou. Consulte startup-error.txt.' }
}
Write-Output ('Aplicativo compilado: ' + (Join-Path $taskOut 'TaskBAR.exe'))
