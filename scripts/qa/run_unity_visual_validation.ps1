param(
    [string]$RustAVRoot = "D:\TestProject\Video\RustAV",

    [string]$CoreRoot = "",

    [string]$UnityProjectRoot = "UnityAVExample",

    [string]$UnityExe = "C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe",

    [string]$ValidationPlayerExe = "",

    [string]$RtspUri = "rtsp://127.0.0.1:8554/mystream",

    [string]$RtmpUri = "rtmp://127.0.0.1:1935/mystream",

    [string]$VideoPath = "TestFiles\SampleVideo_1280x720_10mb.mp4",

    [int]$ValidationSeconds = 8,

    [double]$MinPlaybackSeconds = 2.0,

    [int]$WindowWidth = 0,

    [int]$WindowHeight = 0,

    [double]$AvSyncThresholdMs = 200,

    [int]$AvSyncWarmupSampleCount = 0,

    [string]$FfmpegExe = "C:\Users\HP\Downloads\mediamtx_v1.16.3_windows_amd64\ffmpeg.exe",

    [string]$MediaMtxExe = "",

    [string]$LogDir = "artifacts\unity-validation",

    [switch]$SkipNativeBuild,

    [switch]$SkipPluginSync,

    [switch]$SkipUnityBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-ToolPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$ToolName
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    if (Test-Path $Value) {
        return (Resolve-Path $Value).Path
    }

    $command = Get-Command $Value -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw "[unity-visual-qa] $ToolName not found: $Value"
}

function Wait-ForTcpPort {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Host,

        [Parameter(Mandatory = $true)]
        [int]$Port,

        [int]$TimeoutSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $client = [System.Net.Sockets.TcpClient]::new()
            $async = $client.BeginConnect($Host, $Port, $null, $null)
            if ($async.AsyncWaitHandle.WaitOne(500) -and $client.Connected) {
                $client.EndConnect($async)
                $client.Dispose()
                return
            }
            $client.Dispose()
        }
        catch {
        }

        Start-Sleep -Milliseconds 500
    }

    throw "[unity-visual-qa] timeout waiting for $($Host):$Port"
}

function Start-MediaMtxServer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,

        [Parameter(Mandatory = $true)]
        [string]$StdOutLog,

        [Parameter(Mandatory = $true)]
        [string]$StdErrLog
    )

    $process = Start-Process `
        -FilePath $ExecutablePath `
        -WorkingDirectory ([System.IO.Path]::GetDirectoryName($ExecutablePath)) `
        -RedirectStandardOutput $StdOutLog `
        -RedirectStandardError $StdErrLog `
        -PassThru `
        -WindowStyle Hidden

    Wait-ForTcpPort -Host "127.0.0.1" -Port 8554
    Wait-ForTcpPort -Host "127.0.0.1" -Port 1935
    return $process
}

function Start-FfmpegPublisher {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,

        [Parameter(Mandatory = $true)]
        [string]$Protocol,

        [Parameter(Mandatory = $true)]
        [string]$InputPath,

        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [Parameter(Mandatory = $true)]
        [string]$StdOutLog,

        [Parameter(Mandatory = $true)]
        [string]$StdErrLog
    )

    $args = @(
        "-re",
        "-stream_loop", "-1",
        "-i", $InputPath,
        "-map", "0:v:0",
        "-map", "0:a:0",
        "-c:v", "libx264",
        "-preset", "ultrafast",
        "-tune", "zerolatency",
        "-profile:v", "baseline",
        "-pix_fmt", "yuv420p",
        "-g", "3",
        "-keyint_min", "3",
        "-sc_threshold", "0",
        "-bf", "0",
        "-refs", "1",
        "-b:v", "2500k",
        "-maxrate", "2500k",
        "-bufsize", "100k",
        "-x264-params", "rc-lookahead=0:sync-lookahead=0:repeat-headers=1:force-cfr=1",
        "-c:a", "aac",
        "-b:a", "128k",
        "-ar", "48000",
        "-ac", "2"
    )

    if ($Protocol -eq "rtsp") {
        $args += @(
            "-f", "rtsp",
            "-rtsp_transport", "udp",
            "-muxdelay", "0",
            "-muxpreload", "0",
            $Uri
        )
    }
    elseif ($Protocol -eq "rtmp") {
        $args += @("-f", "flv", $Uri)
    }
    else {
        throw "[unity-visual-qa] unsupported protocol: $Protocol"
    }

    $process = Start-Process `
        -FilePath $ExecutablePath `
        -ArgumentList $args `
        -RedirectStandardOutput $StdOutLog `
        -RedirectStandardError $StdErrLog `
        -PassThru `
        -WindowStyle Hidden

    Start-Sleep -Seconds 2
    if ($process.HasExited) {
        throw "[unity-visual-qa] $Protocol publisher exited early"
    }

    return $process
}

function Stop-BackgroundProcess {
    param(
        [Parameter(Mandatory = $false)]
        [System.Diagnostics.Process]$Process
    )

    if ($null -eq $Process) {
        return
    }

    if (-not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force
        Start-Sleep -Milliseconds 500
    }
}

$resolvedRustRoot = (Resolve-Path $RustAVRoot).Path
$resolvedVideoPath = $VideoPath
if (-not [System.IO.Path]::IsPathRooted($resolvedVideoPath)) {
    $resolvedVideoPath = Join-Path $resolvedRustRoot $resolvedVideoPath
}
$resolvedVideoPath = (Resolve-Path $resolvedVideoPath).Path

$resolvedLogDir = Join-Path $resolvedRustRoot $LogDir
New-Item -ItemType Directory -Force -Path $resolvedLogDir | Out-Null

$resolvedFfmpegExe = Resolve-ToolPath -Value $FfmpegExe -ToolName "ffmpeg"
$resolvedMediaMtxExe = ""
if (-not [string]::IsNullOrWhiteSpace($MediaMtxExe)) {
    $resolvedMediaMtxExe = Resolve-ToolPath -Value $MediaMtxExe -ToolName "mediamtx"
}

$mediamtxOut = Join-Path $resolvedLogDir "mediamtx.out.log"
$mediamtxErr = Join-Path $resolvedLogDir "mediamtx.err.log"
$rtspPublisherOut = Join-Path $resolvedLogDir "rtsp-publisher.out.log"
$rtspPublisherErr = Join-Path $resolvedLogDir "rtsp-publisher.err.log"
$rtmpPublisherOut = Join-Path $resolvedLogDir "rtmp-publisher.out.log"
$rtmpPublisherErr = Join-Path $resolvedLogDir "rtmp-publisher.err.log"

$mediamtxProcess = $null
$rtspPublisher = $null
$rtmpPublisher = $null

try {
    if (-not [string]::IsNullOrWhiteSpace($resolvedMediaMtxExe)) {
        $mediamtxProcess = Start-MediaMtxServer `
            -ExecutablePath $resolvedMediaMtxExe `
            -StdOutLog $mediamtxOut `
            -StdErrLog $mediamtxErr
    }

    $rtspPublisher = Start-FfmpegPublisher `
        -ExecutablePath $resolvedFfmpegExe `
        -Protocol "rtsp" `
        -InputPath $resolvedVideoPath `
        -Uri $RtspUri `
        -StdOutLog $rtspPublisherOut `
        -StdErrLog $rtspPublisherErr

    $rtmpPublisher = Start-FfmpegPublisher `
        -ExecutablePath $resolvedFfmpegExe `
        -Protocol "rtmp" `
        -InputPath $resolvedVideoPath `
        -Uri $RtmpUri `
        -StdOutLog $rtmpPublisherOut `
        -StdErrLog $rtmpPublisherErr

    $scriptArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $resolvedRustRoot "scripts\qa\run_unity_validation.ps1"),
        "-RustAVRoot", $resolvedRustRoot,
        "-CoreRoot", $CoreRoot,
        "-UnityProjectRoot", $UnityProjectRoot,
        "-RtspUri", $RtspUri,
        "-RtmpUri", $RtmpUri,
        "-ValidationSeconds", $ValidationSeconds,
        "-MinPlaybackSeconds", $MinPlaybackSeconds,
        "-AvSyncThresholdMs", $AvSyncThresholdMs,
        "-AvSyncWarmupSampleCount", $AvSyncWarmupSampleCount,
        "-LogDir", $LogDir,
        "-RequireAudioPlayback",
        "-FailOnAvSyncThresholdExceeded"
    )

    if ($WindowWidth -gt 0) {
        $scriptArgs += @("-WindowWidth", $WindowWidth)
    }

    if ($WindowHeight -gt 0) {
        $scriptArgs += @("-WindowHeight", $WindowHeight)
    }

    if (-not [string]::IsNullOrWhiteSpace($ValidationPlayerExe)) {
        $scriptArgs += @(
            "-ValidationPlayerExe", $ValidationPlayerExe,
            "-SkipNativeBuild",
            "-SkipPluginSync",
            "-SkipUnityBuild"
        )
    }
    else {
        $scriptArgs += @("-UnityExe", $UnityExe)

        if ($SkipNativeBuild) {
            $scriptArgs += "-SkipNativeBuild"
        }

        if ($SkipPluginSync) {
            $scriptArgs += "-SkipPluginSync"
        }

        if ($SkipUnityBuild) {
            $scriptArgs += "-SkipUnityBuild"
        }
    }

    & powershell @scriptArgs
    if ($LASTEXITCODE -ne 0) {
        throw "[unity-visual-qa] run_unity_validation failed"
    }
}
finally {
    Stop-BackgroundProcess -Process $rtspPublisher
    Stop-BackgroundProcess -Process $rtmpPublisher
    Stop-BackgroundProcess -Process $mediamtxProcess
}
