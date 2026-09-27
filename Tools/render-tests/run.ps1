[CmdletBinding()]
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe',
    [int]$Width = 960,
    [int]$Height = 540,
    [string]$Scenario = ''
)
$ErrorActionPreference = 'Stop'
& (Join-Path (Split-Path -Parent $PSScriptRoot) 'sync_unity_shared.ps1')
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repo 'UnityProject'
$log = Join-Path $PSScriptRoot 'render-test.log'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity absent : $Unity" }
$args = @('-batchmode','-force-d3d11','-quit',"-projectPath `"$project`"",'-executeMethod','RenderHarness.Run',"-logFile `"$log`"")
if ($Width -lt 128 -or $Width -gt 4096 -or $Height -lt 128 -or $Height -gt 4096) { throw 'Résolution invalide' }
$oldWidth = $env:GE_RENDER_WIDTH
$oldHeight = $env:GE_RENDER_HEIGHT
$oldScenario = $env:GE_RENDER_SCENARIO
$renderStarted = Get-Date
try {
    $env:GE_RENDER_WIDTH = "$Width"
    $env:GE_RENDER_HEIGHT = "$Height"
    $env:GE_RENDER_SCENARIO = $Scenario
    $process = Start-Process -FilePath $Unity -ArgumentList $args -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(1800000)) { throw "Unity ne s'est pas terminé sous 1800 s (PID $($process.Id)) ; voir $log" }
    if ($process.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 90 }
        throw "Harnais Unity en échec ($($process.ExitCode))"
    }
} finally {
    $env:GE_RENDER_WIDTH = $oldWidth
    $env:GE_RENDER_HEIGHT = $oldHeight
    $env:GE_RENDER_SCENARIO = $oldScenario
}
$outRoot = Join-Path $PSScriptRoot $(if ($Width -ge 1920) { 'out-monitor' } else { 'out' })
$timing = Join-Path $PSScriptRoot $(if ($Width -ge 1920) { 'timing-monitor.md' } else { 'timing.md' })
if (-not (Select-String -LiteralPath $log -SimpleMatch '[GroundBlastFx] RenderHarness terminé :' -Quiet) -or
    -not (Test-Path -LiteralPath $timing) -or (Get-Item -LiteralPath $timing).LastWriteTime -lt $renderStarted.AddSeconds(-2)) {
    Get-Content -LiteralPath $log -Tail 50
    throw 'Unity a quitté sans exécuter le harnais (vérifier la licence et le journal)'
}
if ($Scenario) {
    # Un scénario (« S3 ») ou une liste séparée par des virgules (« S3,S7,S13 »).
    foreach ($id in $Scenario.Split(',')) {
        $frames = Join-Path (Join-Path $outRoot $id.Trim()) 'frames.txt'
        if (-not (Test-Path -LiteralPath $frames) -or (Get-Item -LiteralPath $frames).LastWriteTime -lt $renderStarted.AddSeconds(-2)) {
            throw "Scénario non rendu : $id"
        }
    }
}
Write-Host "[render-tests] OK : $PSScriptRoot\$(if ($Width -ge 1920) { 'out-monitor' } else { 'out' })"
