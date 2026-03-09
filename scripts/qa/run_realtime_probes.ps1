param(
    [string]$CoreRoot = "",

    [Parameter(Mandatory = $true)]
    [string]$RtspUri,

    [Parameter(Mandatory = $true)]
    [string]$RtmpUri,

    [int]$Seconds = 10,

    [string]$LogDir = "artifacts/probes"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($CoreRoot)) {
    $CoreRoot = (Get-Location).Path
} else {
    $CoreRoot = (Resolve-Path $CoreRoot).Path
}
function Invoke-Probe {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Example,

        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [Parameter(Mandatory = $true)]
        [string]$LogPath
    )

    Write-Host "[qa] running $Example uri=$Uri seconds=$Seconds"
    $command = "cargo run --manifest-path `"$CoreRoot\Cargo.toml`" --example $Example -- `"$Uri`" $Seconds 2>&1"
    $output = & cmd /c $command | ForEach-Object {
        "$_"
    }
    $output | Tee-Object -FilePath $LogPath | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "[qa] $Example failed, see $LogPath"
    }

    $finalHealth = $output | Select-String "final_health" | Select-Object -Last 1
    $firstFrame = $output | Select-String "first_frame_final=" | Select-Object -Last 1
    $doneLine = $output | Select-String "done uri=" | Select-Object -Last 1

    [pscustomobject]@{
        Example = $Example
        Uri = $Uri
        FirstFrame = if ($firstFrame) { $firstFrame.Line.Trim() } else { "" }
        Done = if ($doneLine) { $doneLine.Line.Trim() } else { "" }
        FinalHealth = if ($finalHealth) { $finalHealth.Line.Trim() } else { "" }
        LogPath = $LogPath
    }
}

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$rtspLog = Join-Path $LogDir "rtsp_probe.log"
$rtmpLog = Join-Path $LogDir "rtmp_probe.log"

$rtspResult = Invoke-Probe -Example "rtsp_probe" -Uri $RtspUri -LogPath $rtspLog
$rtmpResult = Invoke-Probe -Example "rtmp_probe" -Uri $RtmpUri -LogPath $rtmpLog

Write-Host ""
Write-Host "[qa] summary"
Write-Host $rtspResult.FirstFrame
Write-Host $rtspResult.Done
Write-Host $rtspResult.FinalHealth
Write-Host ""
Write-Host $rtmpResult.FirstFrame
Write-Host $rtmpResult.Done
Write-Host $rtmpResult.FinalHealth
