param(
    [string]$RustAVRoot = "D:\TestProject\Video\RustAV",

    [string]$CoreRoot = "",

    [string]$RtspUri = "rtsp://127.0.0.1:8554/mystream",

    [string]$RtmpUri = "rtmp://127.0.0.1:1935/mystream",

    [string]$VideoPath = "TestFiles\SampleVideo_1280x720_10mb.mp4",

    [int]$ValidationSeconds = 8,

    [double]$MinPlaybackSeconds = 2.0,

    [string]$FfmpegExe = "C:\Users\HP\Downloads\mediamtx_v1.16.3_windows_amd64\ffmpeg.exe",

    [string]$MediaMtxExe = "",

    [string]$LogDir = "artifacts\win32-validation"
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

    throw "[win32-qa] $ToolName not found: $Value"
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

    throw "[win32-qa] timeout waiting for $($Host):$Port"
}

function Resolve-CoreRootPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublicRoot,

        [string]$ConfiguredValue = ""
    )

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($ConfiguredValue)) {
        $candidates += $ConfiguredValue
    }
    $candidates += (Join-Path $PublicRoot "..\\RustAV-Core")
    $candidates += $PublicRoot

    foreach ($candidate in $candidates) {
        $resolvedCandidate = $candidate
        if (-not [System.IO.Path]::IsPathRooted($resolvedCandidate)) {
            $resolvedCandidate = Join-Path $PublicRoot $resolvedCandidate
        }
        if (Test-Path $resolvedCandidate) {
            return (Resolve-Path $resolvedCandidate).Path
        }
    }

    throw "[win32-qa] core root not found"
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

function Invoke-PlayerValidation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CoreProjectRoot,

        [Parameter(Mandatory = $true)]
        [string]$CaseName,

        [Parameter(Mandatory = $true)]
        [string]$LogPath,

        [Parameter(Mandatory = $false)]
        [string]$Uri = "",

        [switch]$RequireAudio
    )

    $command = @(
        "cargo run --manifest-path `"$CoreProjectRoot\Cargo.toml`" --example test_player --",
        "--max-seconds=$ValidationSeconds",
        "--min-playback-seconds=$MinPlaybackSeconds",
        "--validate"
    )

    if (-not [string]::IsNullOrWhiteSpace($Uri)) {
        $command += "--uri=`"$Uri`""
    }

    if ($RequireAudio) {
        $command += "--require-audio"
    }

    $fullCommand = ($command -join " ") + " 2>&1"
    Write-Host "[win32-qa] running $CaseName"
    Write-Host "[win32-qa] cmd=$fullCommand"

    $output = & cmd /c $fullCommand | ForEach-Object { "$_" }
    $output | Tee-Object -FilePath $LogPath | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "[win32-qa] $CaseName failed"
    }

    $summary = $output | Select-String "\[test_player summary\]" | Select-Object -Last 1
    if (-not $summary) {
        throw "[win32-qa] $CaseName missing summary"
    }

    return $summary.Line.Trim()
}

function Start-FfmpegPublisher {
    param(
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
    } elseif ($Protocol -eq "rtmp") {
        $args += @("-f", "flv", $Uri)
    } else {
        throw "[win32-qa] unsupported protocol: $Protocol"
    }

    $process = Start-Process `
        -FilePath $script:ResolvedFfmpegExe `
        -ArgumentList $args `
        -RedirectStandardOutput $StdOutLog `
        -RedirectStandardError $StdErrLog `
        -PassThru `
        -WindowStyle Hidden

    Start-Sleep -Seconds 2
    if ($process.HasExited) {
        throw "[win32-qa] $Protocol publisher exited early"
    }

    return $process
}

function Stop-FfmpegPublisher {
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
$resolvedCoreRoot = Resolve-CoreRootPath -PublicRoot $resolvedRustRoot -ConfiguredValue $CoreRoot
$resolvedVideoPath = $VideoPath
if (-not [System.IO.Path]::IsPathRooted($resolvedVideoPath)) {
    $resolvedVideoPath = Join-Path $resolvedRustRoot $resolvedVideoPath
}
$resolvedVideoPath = (Resolve-Path $resolvedVideoPath).Path

$resolvedLogDir = Join-Path $resolvedRustRoot $LogDir
New-Item -ItemType Directory -Force -Path $resolvedLogDir | Out-Null

$script:ResolvedFfmpegExe = Resolve-ToolPath -Value $FfmpegExe -ToolName "ffmpeg"
$resolvedMediaMtxExe = ""
if (-not [string]::IsNullOrWhiteSpace($MediaMtxExe)) {
    $resolvedMediaMtxExe = Resolve-ToolPath -Value $MediaMtxExe -ToolName "mediamtx"
}

$fileLog = Join-Path $resolvedLogDir "file.log"
$rtspLog = Join-Path $resolvedLogDir "rtsp.log"
$rtmpLog = Join-Path $resolvedLogDir "rtmp.log"
$mediamtxOut = Join-Path $resolvedLogDir "mediamtx.out.log"
$mediamtxErr = Join-Path $resolvedLogDir "mediamtx.err.log"
$rtspPublisherOut = Join-Path $resolvedLogDir "rtsp-publisher.out.log"
$rtspPublisherErr = Join-Path $resolvedLogDir "rtsp-publisher.err.log"
$rtmpPublisherOut = Join-Path $resolvedLogDir "rtmp-publisher.out.log"
$rtmpPublisherErr = Join-Path $resolvedLogDir "rtmp-publisher.err.log"

$mediamtxProcess = $null
$rtspPublisher = $null
$rtmpPublisher = $null
$summaries = @()
try {
    if (-not [string]::IsNullOrWhiteSpace($resolvedMediaMtxExe)) {
        $mediamtxProcess = Start-MediaMtxServer `
            -ExecutablePath $resolvedMediaMtxExe `
            -StdOutLog $mediamtxOut `
            -StdErrLog $mediamtxErr
    }

    $summaries += Invoke-PlayerValidation `
        -CoreProjectRoot $resolvedCoreRoot `
        -CaseName "file" `
        -LogPath $fileLog `
        -Uri $resolvedVideoPath `
        -RequireAudio

    $rtspPublisher = Start-FfmpegPublisher `
        -Protocol "rtsp" `
        -InputPath $resolvedVideoPath `
        -Uri $RtspUri `
        -StdOutLog $rtspPublisherOut `
        -StdErrLog $rtspPublisherErr
    $summaries += Invoke-PlayerValidation `
        -CoreProjectRoot $resolvedCoreRoot `
        -CaseName "rtsp" `
        -LogPath $rtspLog `
        -Uri $RtspUri `
        -RequireAudio

    Stop-FfmpegPublisher -Process $rtspPublisher
    $rtspPublisher = $null

    $rtmpPublisher = Start-FfmpegPublisher `
        -Protocol "rtmp" `
        -InputPath $resolvedVideoPath `
        -Uri $RtmpUri `
        -StdOutLog $rtmpPublisherOut `
        -StdErrLog $rtmpPublisherErr
    $summaries += Invoke-PlayerValidation `
        -CoreProjectRoot $resolvedCoreRoot `
        -CaseName "rtmp" `
        -LogPath $rtmpLog `
        -Uri $RtmpUri `
        -RequireAudio
}
finally {
    Stop-FfmpegPublisher -Process $rtspPublisher
    Stop-FfmpegPublisher -Process $rtmpPublisher
    if ($null -ne $mediamtxProcess -and -not $mediamtxProcess.HasExited) {
        Stop-Process -Id $mediamtxProcess.Id -Force
    }
}

Write-Host ""
Write-Host "[win32-qa] summary"
$summaries | ForEach-Object { Write-Host $_ }
