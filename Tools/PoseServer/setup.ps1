$ErrorActionPreference = 'Stop'
$serverDir = $PSScriptRoot
$venvPython = Join-Path $serverDir '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    & py -3.11 -m venv (Join-Path $serverDir '.venv')
    if ($LASTEXITCODE -ne 0) { throw 'Python 3.11 is required. Install it, then rerun setup.' }
}
& $venvPython -m pip install -r (Join-Path $serverDir 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
$modelDir = Join-Path $serverDir 'models'
New-Item -ItemType Directory -Force -Path $modelDir | Out-Null
$modelFile = Join-Path $modelDir 'hand_landmarker.task'
if (-not (Test-Path -LiteralPath $modelFile)) {
    $downloadFile = Join-Path $modelDir 'hand_landmarker.task.download'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task' -OutFile $downloadFile
    if ((Get-Item -LiteralPath $downloadFile).Length -lt 1000000) { throw 'Model download is incomplete.' }
    Move-Item -LiteralPath $downloadFile -Destination $modelFile
}
$wristModel = Join-Path $modelDir 'pose_landmarker_lite.task'
if (-not (Test-Path -LiteralPath $wristModel)) {
    $wristDownload = Join-Path $modelDir 'pose_landmarker_lite.task.download'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task' -OutFile $wristDownload
    if ((Get-Item -LiteralPath $wristDownload).Length -lt 1000000) { throw 'Wrist fallback model download is incomplete.' }
    Move-Item -LiteralPath $wristDownload -Destination $wristModel
}
Write-Host 'Setup complete (Hands + wrist fallback). No camera was opened. Run start.ps1, then connect from Unity.'
