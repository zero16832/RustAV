param(
    [string]$ProjectRoot = "D:\TestProject\Video\RustAV",

    [string]$CoreRoot = "",

    [string]$LogDir = "artifacts\production-gate",

    [string]$SampleUri = "TestFiles\SampleVideo_1280x720_10mb.mp4",

    [int]$SampleSeconds = 2,

    [string]$RtspUri = "",

    [string]$RtmpUri = "",

    [int]$RealtimeSeconds = 3,

    [string]$RtspAvUri = "",

    [string]$RtmpAvUri = "",

    [int]$AvSeconds = 60,

    [string]$UnityRtspUri = "",

    [string]$UnityRtmpUri = "",

    [int]$UnitySeconds = 600,

    [double]$UnityAvSyncThresholdMs = 200,

    [int]$UnityAvSyncWarmupSampleCount = 5
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string]$LogPath
    )

    Write-Host ""
    Write-Host "[gate] running $Name"
    Write-Host "[gate] cmd=$Command"

    $tmpOutputPath = Join-Path $env:TEMP ("rustav-gate-" + [guid]::NewGuid().ToString("N") + ".log")
    try {
        & cmd /c "$Command > `"$tmpOutputPath`" 2>&1"
        $output = if (Test-Path $tmpOutputPath) {
            Get-Content $tmpOutputPath
        } else {
            @()
        }
    }
    finally {
        if (Test-Path $tmpOutputPath) {
            Remove-Item -Path $tmpOutputPath -Force -ErrorAction SilentlyContinue
        }
    }
    $output | Tee-Object -FilePath $LogPath | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "[gate] $Name failed, see $LogPath"
    }

    return $output
}

function Get-AbsoluteLogPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    return (Join-Path $Root $RelativePath)
}

$resolvedRoot = (Resolve-Path $ProjectRoot).Path
if ([string]::IsNullOrWhiteSpace($CoreRoot)) {
    $coreRootCandidate = Join-Path $resolvedRoot "..\\RustAV-Core"
    if (Test-Path $coreRootCandidate) {
        $resolvedCoreRoot = (Resolve-Path $coreRootCandidate).Path
    } else {
        $resolvedCoreRoot = $resolvedRoot
    }
} else {
    $resolvedCoreRoot = (Resolve-Path $CoreRoot).Path
}

$resolvedSampleUri = $SampleUri
if (-not [System.IO.Path]::IsPathRooted($resolvedSampleUri)) {
    $resolvedSampleUri = Join-Path $resolvedRoot $resolvedSampleUri
}
Set-Location $resolvedRoot

$resolvedLogDir = Get-AbsoluteLogPath -Root $resolvedRoot -RelativePath $LogDir
New-Item -ItemType Directory -Force -Path $resolvedLogDir | Out-Null

$summary = New-Object System.Collections.Generic.List[string]

$null = Invoke-Step `
    -Name "cargo-check" `
    -Command "cargo check --manifest-path `"$resolvedCoreRoot\Cargo.toml`" --lib --examples --locked" `
    -LogPath (Join-Path $resolvedLogDir "cargo-check.log")
$summary.Add("cargo-check=ok")

$null = Invoke-Step `
    -Name "cargo-test" `
    -Command "cargo test --manifest-path `"$resolvedCoreRoot\Cargo.toml`" --lib --tests --locked" `
    -LogPath (Join-Path $resolvedLogDir "cargo-test.log")
$summary.Add("cargo-test=ok")

$null = Invoke-Step `
    -Name "ios-staticlib-check" `
    -Command "cargo check --manifest-path `"$resolvedCoreRoot\ios-staticlib\Cargo.toml`" --lib --locked" `
    -LogPath (Join-Path $resolvedLogDir "ios-staticlib-check.log")
$summary.Add("ios-staticlib-check=ok")

$null = Invoke-Step `
    -Name "ci-entrypoints" `
    -Command "python scripts/ci/validate_ci_entrypoints.py --public-root `"$resolvedRoot`" --core-root `"$resolvedCoreRoot`"" `
    -LogPath (Join-Path $resolvedLogDir "ci-entrypoints.log")
$summary.Add("ci-entrypoints=ok")

$audioOutput = Invoke-Step `
    -Name "audio-probe" `
    -Command "cargo run --manifest-path `"$resolvedCoreRoot\Cargo.toml`" --example audio_probe -- `"$resolvedSampleUri`" $SampleSeconds" `
    -LogPath (Join-Path $resolvedLogDir "audio-probe.log")
$summary.Add("audio-probe=ok")

$audioFirstVideo = $audioOutput | Select-String "first_video_final=" | Select-Object -Last 1
$audioFirstAudio = $audioOutput | Select-String "first_audio_final=" | Select-Object -Last 1
if ($audioFirstVideo) { $summary.Add($audioFirstVideo.Line.Trim()) }
if ($audioFirstAudio) { $summary.Add($audioFirstAudio.Line.Trim()) }

$hasRealtimeUris = -not [string]::IsNullOrWhiteSpace($RtspUri) -and -not [string]::IsNullOrWhiteSpace($RtmpUri)
if ($hasRealtimeUris) {
    $null = Invoke-Step `
        -Name "realtime-probes" `
        -Command "powershell -ExecutionPolicy Bypass -File scripts/qa/run_realtime_probes.ps1 -CoreRoot `"$resolvedCoreRoot`" -RtspUri `"$RtspUri`" -RtmpUri `"$RtmpUri`" -Seconds $RealtimeSeconds -LogDir `"$resolvedLogDir\realtime`"" `
        -LogPath (Join-Path $resolvedLogDir "realtime-probes.log")
    $summary.Add("realtime-probes=ok")
} else {
    $summary.Add("realtime-probes=skipped")
}

$hasAvUris = -not [string]::IsNullOrWhiteSpace($RtspAvUri) -and -not [string]::IsNullOrWhiteSpace($RtmpAvUri)
if ($hasAvUris) {
    $null = Invoke-Step `
        -Name "av-soak" `
        -Command "powershell -ExecutionPolicy Bypass -File scripts/qa/run_av_soak.ps1 -CoreRoot `"$resolvedCoreRoot`" -RtspUri `"$RtspAvUri`" -RtmpUri `"$RtmpAvUri`" -Seconds $AvSeconds -LogDir `"$resolvedLogDir\av-soak`"" `
        -LogPath (Join-Path $resolvedLogDir "av-soak.log")
    $summary.Add("av-soak=ok")
} else {
    $summary.Add("av-soak=skipped")
}

$hasUnityUris = -not [string]::IsNullOrWhiteSpace($UnityRtspUri) -and -not [string]::IsNullOrWhiteSpace($UnityRtmpUri)
if ($hasUnityUris) {
    $null = Invoke-Step `
        -Name "unity-soak" `
        -Command "powershell -ExecutionPolicy Bypass -File scripts/qa/run_unity_validation.ps1 -RustAVRoot `"$resolvedRoot`" -CoreRoot `"$resolvedCoreRoot`" -UnityProjectRoot `"$resolvedRoot\UnityAVExample`" -RtspUri `"$UnityRtspUri`" -RtmpUri `"$UnityRtmpUri`" -ValidationSeconds $UnitySeconds -SkipFileCase -AvSyncThresholdMs $UnityAvSyncThresholdMs -AvSyncWarmupSampleCount $UnityAvSyncWarmupSampleCount -FailOnAvSyncThresholdExceeded -LogDir `"$resolvedLogDir\unity-soak`"" `
        -LogPath (Join-Path $resolvedLogDir "unity-soak.log")
    $summary.Add("unity-soak=ok")
} else {
    $summary.Add("unity-soak=skipped")
}

$summaryPath = Join-Path $resolvedLogDir "summary.txt"
$summary | Set-Content -Path $summaryPath

Write-Host ""
Write-Host "[gate] summary"
$summary | ForEach-Object { Write-Host $_ }
Write-Host "[gate] summary_path=$summaryPath"
