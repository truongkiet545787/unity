param([int]$CameraIndex = 0, [ValidateSet('dshow','msmf')][string]$Backend = 'dshow')
$ErrorActionPreference = 'Stop'
$venvPython = Join-Path $PSScriptRoot '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) { throw 'Run Tools/PoseServer/setup.ps1 first.' }
& $venvPython (Join-Path $PSScriptRoot 'server.py') --camera $CameraIndex --backend $Backend
if ($LASTEXITCODE -ne 0) { throw 'Pose server stopped with an error. See the message above.' }
