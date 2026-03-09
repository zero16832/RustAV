param(
    [string]$CoreRoot = "",

    [Parameter(Mandatory = $true)]
    [string]$RtspUri,

    [Parameter(Mandatory = $true)]
    [string]$RtmpUri,

    [int]$Seconds = 60,

    [int]$Width = 1280,

    [int]$Height = 720,

    [string]$LogDir = "artifacts/av-soak"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($CoreRoot)) {
    $CoreRoot = (Get-Location).Path
} else {
    $CoreRoot = (Resolve-Path $CoreRoot).Path
}

function Invoke-AudioProbe {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [Parameter(Mandatory = $true)]
        [string]$LogPath
    )

    Write-Host "[qa-av] running audio_probe label=$Label uri=$Uri seconds=$Seconds width=$Width height=$Height"
    $command = "cargo run --manifest-path `"$CoreRoot\Cargo.toml`" --example audio_probe -- `"$Uri`" $Seconds $Width $Height 2>&1"
    $output = & cmd /c $command | ForEach-Object {
        "$_"
    }
    $output | Tee-Object -FilePath $LogPath | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "[qa-av] $Label failed, see $LogPath"
    }

    $firstVideo = $output | Select-String "first_video_final=" | Select-Object -Last 1
    $firstAudio = $output | Select-String "first_audio_final=" | Select-Object -Last 1
    $doneLine = $output | Select-String "done uri=" | Select-Object -Last 1
    $finalHealth = $output | Select-String "final_health" | Select-Object -Last 1

    [pscustomobject]@{
        Label = $Label
        Uri = $Uri
        FirstVideo = if ($firstVideo) { $firstVideo.Line.Trim() } else { "" }
        FirstAudio = if ($firstAudio) { $firstAudio.Line.Trim() } else { "" }
        Done = if ($doneLine) { $doneLine.Line.Trim() } else { "" }
        FinalHealth = if ($finalHealth) { $finalHealth.Line.Trim() } else { "" }
        LogPath = $LogPath
    }
}

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$rtspLog = Join-Path $LogDir "rtsp_audio_probe.log"
$rtmpLog = Join-Path $LogDir "rtmp_audio_probe.log"

$rtspResult = Invoke-AudioProbe -Label "rtsp" -Uri $RtspUri -LogPath $rtspLog
$rtmpResult = Invoke-AudioProbe -Label "rtmp" -Uri $RtmpUri -LogPath $rtmpLog

Write-Host ""
Write-Host "[qa-av] summary"
Write-Host $rtspResult.FirstVideo
Write-Host $rtspResult.FirstAudio
Write-Host $rtspResult.Done
Write-Host $rtspResult.FinalHealth
Write-Host ""
Write-Host $rtmpResult.FirstVideo
Write-Host $rtmpResult.FirstAudio
Write-Host $rtmpResult.Done
Write-Host $rtmpResult.FinalHealth
