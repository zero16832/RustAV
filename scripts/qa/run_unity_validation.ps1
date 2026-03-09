param(
    [string]$RustAVRoot = "D:\TestProject\Video\RustAV",

    [string]$UnityProjectRoot = "UnityAVExample",

    [string]$UnityExe = "C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe",

    [string]$ValidationPlayerExe = "",

    [string]$RtspUri = "",

    [string]$RtmpUri = "",

    [int]$ValidationSeconds = 6,

    [int]$WindowWidth = 0,

    [int]$WindowHeight = 0,

    [double]$AvSyncThresholdMs = 200,

    [int]$AvSyncWarmupSampleCount = 0,

    [double]$MinPlaybackSeconds = 1.0,

    [switch]$FailOnAvSyncThresholdExceeded,

    [switch]$RequireAudioPlayback,

    [switch]$SkipNativeBuild,

    [switch]$SkipPluginSync,

    [switch]$SkipUnityBuild,

    [switch]$SkipFileCase,

    [switch]$SkipRtspCase,

    [switch]$SkipRtmpCase,

    [string]$LogDir = "artifacts\unity-validation"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$InvariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [string]$LogPath = ""
    )

    Write-Host ""
    Write-Host "[unity-qa] running $Name"
    Write-Host "[unity-qa] cmd=$Command"

    $tmpOutputPath = Join-Path $env:TEMP ("rustav-unity-qa-" + [guid]::NewGuid().ToString("N") + ".log")
    Push-Location $WorkingDirectory
    try {
        & cmd /c "$Command > `"$tmpOutputPath`" 2>&1"
        $output = if (Test-Path $tmpOutputPath) { Get-Content $tmpOutputPath } else { @() }
    }
    finally {
        Pop-Location
        if (Test-Path $tmpOutputPath) {
            Remove-Item -Path $tmpOutputPath -Force -ErrorAction SilentlyContinue
        }
    }

    if ($LogPath -ne "") {
        $output | Tee-Object -FilePath $LogPath | Out-Null
    }

    if ($LASTEXITCODE -ne 0) {
        throw "[unity-qa] $Name failed"
    }

    return $output
}

function Sync-UnityPlugins {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RustRoot,

        [Parameter(Mandatory = $true)]
        [string]$UnityProject
    )

    $pluginsSrc = Join-Path $RustRoot "target\unity-package\windows\Assets\Plugins"
    $pluginsDst = Join-Path $UnityProject "Assets\Plugins"

    if (-not (Test-Path $pluginsSrc)) {
        throw "[unity-qa] plugins source not found: $pluginsSrc"
    }

    if (-not (Test-Path $pluginsDst)) {
        New-Item -ItemType Directory -Force -Path $pluginsDst | Out-Null
    }

    $preservedNames = @()
    Get-ChildItem -Force -Path $pluginsDst | ForEach-Object {
        if ($preservedNames -contains $_.Name) {
            return
        }

        Remove-Item -Path $_.FullName -Recurse -Force
    }

    Get-ChildItem -Force -Path $pluginsSrc | ForEach-Object {
        $target = Join-Path $pluginsDst $_.Name
        if (Test-Path $target) {
            Remove-Item -Path $target -Recurse -Force
        }

        Copy-Item -Path $_.FullName -Destination $target -Recurse -Force
    }

    Get-ChildItem -Path $pluginsDst -Recurse -Filter "DEPENDENCIES.txt" -File | Remove-Item -Force
}

function Invoke-ValidationPlayer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExePath,

        [Parameter(Mandatory = $true)]
        [string]$CaseName,

        [Parameter(Mandatory = $true)]
        [string]$LogPath,

        [string]$Uri = "",

        [int]$Seconds = 6,

        [int]$WindowWidthValue = 0,

        [int]$WindowHeightValue = 0
    )

    $args = @(
        "-logFile", $LogPath,
        "-validationSeconds=$Seconds"
    )

    if (-not [string]::IsNullOrWhiteSpace($Uri)) {
        $args += "-uri=$Uri"
    }

    if ($WindowWidthValue -gt 0) {
        $args += "-windowWidth=$WindowWidthValue"
    }

    if ($WindowHeightValue -gt 0) {
        $args += "-windowHeight=$WindowHeightValue"
    }

    $process = Start-Process -FilePath $ExePath -ArgumentList $args -PassThru -WindowStyle Normal
    $timeoutMs = [Math]::Max(25000, (($Seconds * 2) + 20) * 1000)
    if (-not $process.WaitForExit($timeoutMs)) {
        Stop-Process -Id $process.Id -Force
        throw "[unity-qa] $CaseName timeout"
    }

    if ($process.ExitCode -ne 0) {
        throw "[unity-qa] $CaseName exit code=$($process.ExitCode)"
    }
}

function Get-AvSyncStats {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines,

        [int]$WarmupSampleCount = 0
    )

    $deltaValues = New-Object System.Collections.Generic.List[double]
    foreach ($line in $Lines) {
        if ($line -match "av_sync .*delta_ms=([-+]?[0-9]*\.?[0-9]+)") {
            $deltaValues.Add([double]::Parse($Matches[1], $InvariantCulture))
        }
    }

    $rawCount = $deltaValues.Count
    $effectiveValues = @($deltaValues.ToArray())
    if ($WarmupSampleCount -gt 0 -and $rawCount -gt $WarmupSampleCount) {
        $effectiveValues = @($effectiveValues[$WarmupSampleCount..($rawCount - 1)])
    } elseif ($WarmupSampleCount -gt 0 -and $rawCount -le $WarmupSampleCount) {
        $effectiveValues = @()
    }

    if ($effectiveValues.Count -eq 0) {
        return [pscustomobject]@{
            RawCount = $rawCount
            Count = 0
            Min = $null
            Max = $null
            MaxAbs = $null
            Avg = $null
            P95Abs = $null
            Latest = $null
        }
    }

    $deltaArray = $effectiveValues
    $absArray = @($deltaArray | ForEach-Object { [Math]::Abs($_) } | Sort-Object)
    $p95Index = [Math]::Ceiling($absArray.Count * 0.95) - 1
    if ($p95Index -lt 0) {
        $p95Index = 0
    }

    return [pscustomobject]@{
        RawCount = $rawCount
        Count = $deltaArray.Length
        Min = [Math]::Round(($deltaArray | Measure-Object -Minimum).Minimum, 1)
        Max = [Math]::Round(($deltaArray | Measure-Object -Maximum).Maximum, 1)
        MaxAbs = [Math]::Round(($absArray[$absArray.Count - 1]), 1)
        Avg = [Math]::Round(($deltaArray | Measure-Object -Average).Average, 2)
        P95Abs = [Math]::Round($absArray[$p95Index], 1)
        Latest = [Math]::Round($deltaArray[$deltaArray.Length - 1], 1)
    }
}

function Get-PlaybackStats {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $tickCount = 0
    $maxTimeSec = -1.0
    $hasTexture = $false
    $hasAudioPlaying = $false

    foreach ($line in $Lines) {
        if ($line -match "\[CodexValidation\] time=([-+]?[0-9]*\.?[0-9]+)s texture=(True|False) audioPlaying=(True|False)") {
            $tickCount += 1
            $timeSec = [double]::Parse($Matches[1], $InvariantCulture)
            if ($timeSec -gt $maxTimeSec) {
                $maxTimeSec = $timeSec
            }

            if ($Matches[2] -eq "True") {
                $hasTexture = $true
            }

            if ($Matches[3] -eq "True") {
                $hasAudioPlaying = $true
            }
        }
    }

    return [pscustomobject]@{
        TickCount = $tickCount
        MaxTimeSec = if ($tickCount -gt 0) { [Math]::Round($maxTimeSec, 3) } else { $null }
        HasTexture = $hasTexture
        HasAudioPlaying = $hasAudioPlaying
    }
}

function Format-NullableNumber {
    param(
        [Parameter(Mandatory = $false)]
        [Nullable[double]]$Value,

        [string]$Suffix = ""
    )

    if ($null -eq $Value) {
        return "n/a"
    }

    return ($Value.ToString($InvariantCulture) + $Suffix)
}

function Get-ValidationSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CaseName,

        [Parameter(Mandatory = $true)]
        [string]$LogPath,

        [Parameter(Mandatory = $true)]
        [double]$ThresholdMs
    )

    $lines = @()
    if (Test-Path $LogPath) {
        $lines = @(Get-Content -Path $LogPath)
    }
    $windowLine = ($lines | Select-String "\[CodexValidation\] window_configured=" | Select-Object -Last 1)
    $timeLine = ($lines | Select-String "\[CodexValidation\] time=" | Select-Object -Last 1)
    $completeLine = ($lines | Select-String "\[CodexValidation\] complete" | Select-Object -Last 1)

    $analysisLines = $lines
    if ($completeLine) {
        $completeIndex = [Math]::Max(0, $completeLine.LineNumber - 1)
        $analysisLines = @($lines[0..$completeIndex])
    }

    $avSyncStats = Get-AvSyncStats -Lines $analysisLines -WarmupSampleCount $AvSyncWarmupSampleCount
    $playbackStats = Get-PlaybackStats -Lines $analysisLines
    $playbackValidated = [bool]$completeLine `
        -and $playbackStats.TickCount -gt 0 `
        -and $playbackStats.HasTexture `
        -and $null -ne $playbackStats.MaxTimeSec `
        -and $playbackStats.MaxTimeSec -ge $MinPlaybackSeconds

    [pscustomobject]@{
        Case = $CaseName
        Window = if ($windowLine) { $windowLine.Line.Trim() } else { "" }
        LastTick = if ($timeLine) { $timeLine.Line.Trim() } else { "" }
        Completed = [bool]$completeLine
        TickCount = $playbackStats.TickCount
        MaxTimeSec = $playbackStats.MaxTimeSec
        HasTexture = $playbackStats.HasTexture
        HasAudioPlaying = $playbackStats.HasAudioPlaying
        MinPlaybackSeconds = $MinPlaybackSeconds
        PlaybackValidated = $playbackValidated
        AvSyncWarmupSamples = $AvSyncWarmupSampleCount
        AvSyncRawCount = $avSyncStats.RawCount
        AvSyncCount = $avSyncStats.Count
        AvSyncMinMs = $avSyncStats.Min
        AvSyncMaxMs = $avSyncStats.Max
        AvSyncMaxAbsMs = $avSyncStats.MaxAbs
        AvSyncAvgMs = $avSyncStats.Avg
        AvSyncP95AbsMs = $avSyncStats.P95Abs
        AvSyncLatestMs = $avSyncStats.Latest
        AvSyncThresholdMs = $ThresholdMs
        AvSyncWithinThreshold = ($avSyncStats.Count -gt 0 -and $null -ne $avSyncStats.MaxAbs -and $avSyncStats.MaxAbs -le $ThresholdMs)
        LogPath = $LogPath
    }
}

$resolvedRustRoot = (Resolve-Path $RustAVRoot).Path
$unityProjectCandidate = $UnityProjectRoot
if (-not [System.IO.Path]::IsPathRooted($unityProjectCandidate)) {
    $unityProjectCandidate = Join-Path $resolvedRustRoot $unityProjectCandidate
}
$resolvedUnityProjectRoot = (Resolve-Path $unityProjectCandidate).Path
$resolvedLogDir = Join-Path $resolvedRustRoot $LogDir
$resolvedUnityExe = ""
$resolvedValidationPlayerExe = ""

if (-not [string]::IsNullOrWhiteSpace($ValidationPlayerExe)) {
    $resolvedValidationPlayerExe = (Resolve-Path $ValidationPlayerExe).Path
}

if (-not $SkipUnityBuild -and [string]::IsNullOrWhiteSpace($resolvedValidationPlayerExe)) {
    $resolvedUnityExe = (Resolve-Path $UnityExe).Path
}

New-Item -ItemType Directory -Force -Path $resolvedLogDir | Out-Null

$buildLog = Join-Path $resolvedLogDir "unity-build.log"
$nativeLog = Join-Path $resolvedLogDir "native-build.log"
$unityBatchLog = Join-Path $resolvedUnityProjectRoot "Build\codex-unity-build.log"

if (-not $SkipNativeBuild) {
    Invoke-Step `
        -Name "native-build" `
        -Command "python scripts/ci/build_unity_plugins.py --project-root `"$resolvedRustRoot`" --platform windows --output-root `"$resolvedRustRoot\target\unity-package\windows`"" `
        -WorkingDirectory $resolvedRustRoot `
        -LogPath $nativeLog | Out-Null
}

if (-not $SkipPluginSync) {
    Sync-UnityPlugins `
        -RustRoot $resolvedRustRoot `
        -UnityProject $resolvedUnityProjectRoot
}

if (-not $SkipUnityBuild) {
    Invoke-Step `
        -Name "unity-batch-build" `
        -Command "`"$resolvedUnityExe`" -batchmode -quit -nographics -projectPath `"$resolvedUnityProjectRoot`" -logFile `"$unityBatchLog`" -executeMethod UnityAV.Editor.CodexValidationBuild.BuildWindowsValidationPlayer" `
        -WorkingDirectory $resolvedUnityProjectRoot `
        -LogPath $buildLog | Out-Null
}

$playerExe = $resolvedValidationPlayerExe
if ([string]::IsNullOrWhiteSpace($playerExe)) {
    $playerExe = Join-Path $resolvedUnityProjectRoot "Build\CodexPullValidation\CodexPullValidation.exe"
}
if (-not (Test-Path $playerExe)) {
    throw "[unity-qa] player exe not found: $playerExe"
}

Get-Process CodexPullValidation -ErrorAction SilentlyContinue | Stop-Process -Force

$cases = @()

if (-not $SkipFileCase) {
    $cases += @{
        Name = "file"
        Uri = ""
    }
}

if (-not $SkipRtspCase -and -not [string]::IsNullOrWhiteSpace($RtspUri)) {
    $cases += @{
        Name = "rtsp"
        Uri = $RtspUri
    }
}

if (-not $SkipRtmpCase -and -not [string]::IsNullOrWhiteSpace($RtmpUri)) {
    $cases += @{
        Name = "rtmp"
        Uri = $RtmpUri
    }
}

if ($cases.Count -eq 0) {
    throw "[unity-qa] no validation cases selected"
}

$summaries = @()
foreach ($case in $cases) {
    $caseLog = Join-Path $resolvedLogDir ($case.Name + ".log")
    Invoke-ValidationPlayer `
        -ExePath $playerExe `
        -CaseName $case.Name `
        -LogPath $caseLog `
        -Uri $case.Uri `
        -Seconds $ValidationSeconds `
        -WindowWidthValue $WindowWidth `
        -WindowHeightValue $WindowHeight

    $summaries += Get-ValidationSummary -CaseName $case.Name -LogPath $caseLog -ThresholdMs $AvSyncThresholdMs
}

$summaryPath = Join-Path $resolvedLogDir "summary.txt"
$summaries | ForEach-Object {
    $_.Case
    $_.Window
    $_.LastTick
    ("completed=" + $_.Completed)
    ("tick_count=" + $_.TickCount)
    ("max_time_sec=" + (Format-NullableNumber $_.MaxTimeSec))
    ("has_texture=" + $_.HasTexture)
    ("has_audio_playing=" + $_.HasAudioPlaying)
    ("min_playback_seconds=" + (Format-NullableNumber $_.MinPlaybackSeconds))
    ("playback_validated=" + $_.PlaybackValidated)
    ("av_sync_warmup_samples=" + $_.AvSyncWarmupSamples)
    ("av_sync_raw_count=" + $_.AvSyncRawCount)
    ("av_sync_count=" + $_.AvSyncCount)
    ("av_sync_min_ms=" + (Format-NullableNumber $_.AvSyncMinMs))
    ("av_sync_max_ms=" + (Format-NullableNumber $_.AvSyncMaxMs))
    ("av_sync_max_abs_ms=" + (Format-NullableNumber $_.AvSyncMaxAbsMs))
    ("av_sync_avg_ms=" + (Format-NullableNumber $_.AvSyncAvgMs))
    ("av_sync_p95_abs_ms=" + (Format-NullableNumber $_.AvSyncP95AbsMs))
    ("av_sync_latest_ms=" + (Format-NullableNumber $_.AvSyncLatestMs))
    ("av_sync_threshold_ms=" + (Format-NullableNumber $_.AvSyncThresholdMs))
    ("av_sync_within_threshold=" + $_.AvSyncWithinThreshold)
    ("log=" + $_.LogPath)
    ""
} | Set-Content -Path $summaryPath

Write-Host ""
Write-Host "[unity-qa] summary"
$summaries | ForEach-Object {
    Write-Host ("case=" + $_.Case)
    Write-Host ("  " + $_.Window)
    Write-Host ("  " + $_.LastTick)
    Write-Host ("  completed=" + $_.Completed)
    Write-Host ("  tick_count=" + $_.TickCount)
    Write-Host ("  max_time_sec=" + (Format-NullableNumber $_.MaxTimeSec))
    Write-Host ("  has_texture=" + $_.HasTexture)
    Write-Host ("  has_audio_playing=" + $_.HasAudioPlaying)
    Write-Host ("  min_playback_seconds=" + (Format-NullableNumber $_.MinPlaybackSeconds))
    Write-Host ("  playback_validated=" + $_.PlaybackValidated)
    Write-Host ("  av_sync_warmup_samples=" + $_.AvSyncWarmupSamples)
    Write-Host ("  av_sync_raw_count=" + $_.AvSyncRawCount)
    Write-Host ("  av_sync_count=" + $_.AvSyncCount)
    Write-Host ("  av_sync_min_ms=" + (Format-NullableNumber $_.AvSyncMinMs))
    Write-Host ("  av_sync_max_ms=" + (Format-NullableNumber $_.AvSyncMaxMs))
    Write-Host ("  av_sync_max_abs_ms=" + (Format-NullableNumber $_.AvSyncMaxAbsMs))
    Write-Host ("  av_sync_avg_ms=" + (Format-NullableNumber $_.AvSyncAvgMs))
    Write-Host ("  av_sync_p95_abs_ms=" + (Format-NullableNumber $_.AvSyncP95AbsMs))
    Write-Host ("  av_sync_latest_ms=" + (Format-NullableNumber $_.AvSyncLatestMs))
    Write-Host ("  av_sync_threshold_ms=" + (Format-NullableNumber $_.AvSyncThresholdMs))
    Write-Host ("  av_sync_within_threshold=" + $_.AvSyncWithinThreshold)
    Write-Host ("  log=" + $_.LogPath)
}
Write-Host "[unity-qa] summary_path=$summaryPath"

$playbackFailedCases = @($summaries | Where-Object { -not $_.PlaybackValidated })
if ($playbackFailedCases.Count -gt 0) {
    $failedNames = ($playbackFailedCases | ForEach-Object { $_.Case }) -join ","
    throw "[unity-qa] playback validation failed: $failedNames"
}

if ($RequireAudioPlayback) {
    $audioFailedCases = @($summaries | Where-Object { -not $_.HasAudioPlaying })
    if ($audioFailedCases.Count -gt 0) {
        $failedNames = ($audioFailedCases | ForEach-Object { $_.Case }) -join ","
        throw "[unity-qa] audio playback validation failed: $failedNames"
    }
}

if ($FailOnAvSyncThresholdExceeded) {
    $failedCases = @($summaries | Where-Object { $_.AvSyncCount -le 0 -or -not $_.AvSyncWithinThreshold })
    if ($failedCases.Count -gt 0) {
        $failedNames = ($failedCases | ForEach-Object { $_.Case }) -join ","
        throw "[unity-qa] av_sync threshold exceeded or missing samples: $failedNames"
    }
}
Write-Host "[unity-qa] summary_path=$summaryPath"
