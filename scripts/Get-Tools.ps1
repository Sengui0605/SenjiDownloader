param(
    [Parameter(Mandatory=$true)][string]$Destination,
    [string]$ExistingFfmpegDirectory = '',
    [string]$PreparedEngines = ''
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$headers = @{'User-Agent'='SenjiDownloader-build'}
function Download([string]$Url,[string]$Path) {
    for ($attempt=1; $attempt -le 3; $attempt++) {
        try { Invoke-WebRequest -Uri $Url -OutFile $Path -Headers $headers -TimeoutSec 600; return }
        catch { if ($attempt -eq 3) { throw }; Start-Sleep -Seconds (2*$attempt) }
    }
}
function Verify([string]$Path,[string]$Hash) {
    if ($Hash -notmatch '^[a-fA-F0-9]{64}$') { throw 'Checksum SHA-256 ausente o inválido.' }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash) { throw ('Checksum incorrecto: ' + $Path) }
}
$scratch = Join-Path ([IO.Path]::GetFullPath($Destination)) ('build-temp-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
if ($PreparedEngines -and (Test-Path -LiteralPath (Join-Path $PreparedEngines 'yt-dlp.exe'))) {
    Copy-Item -LiteralPath (Join-Path $PreparedEngines 'yt-dlp.exe') -Destination (Join-Path $Destination 'yt-dlp.exe')
    Copy-Item -LiteralPath (Join-Path $PreparedEngines 'deno\deno.exe') -Destination (Join-Path $Destination 'deno.exe')
    $ytVersion = (Get-Content -LiteralPath (Join-Path $PreparedEngines 'ytdlp-version.txt') -Raw).Trim()
    $denoVersion = (Get-Content -LiteralPath (Join-Path $PreparedEngines 'deno-version.txt') -Raw).Trim()
} else {
    $yt = Invoke-RestMethod -Uri 'https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest' -Headers $headers -TimeoutSec 60
    $asset = $yt.assets | Where-Object name -eq 'yt-dlp.exe'
    Download $asset.browser_download_url (Join-Path $Destination 'yt-dlp.exe')
    $hash = if ($asset.digest -like 'sha256:*') { $asset.digest.Substring(7) } else {
        $sums = Invoke-WebRequest -Uri ($yt.assets | Where-Object name -eq 'SHA2-256SUMS').browser_download_url -Headers $headers
        (($sums.Content -split "`n" | Where-Object { $_ -match '\s\*?yt-dlp\.exe$' }) -split '\s+')[0]
    }
    Verify (Join-Path $Destination 'yt-dlp.exe') $hash
    $ytVersion = $yt.tag_name
    $deno = Invoke-RestMethod -Uri 'https://api.github.com/repos/denoland/deno/releases/latest' -Headers $headers -TimeoutSec 60
    $denoAsset = $deno.assets | Where-Object name -eq 'deno-x86_64-pc-windows-msvc.zip'
    $denoZip = Join-Path $scratch 'deno.zip'
    Download $denoAsset.browser_download_url $denoZip
    Verify $denoZip $denoAsset.digest.Substring(7)
    Expand-Archive -LiteralPath $denoZip -DestinationPath (Join-Path $scratch 'deno')
    Copy-Item -LiteralPath (Join-Path $scratch 'deno\deno.exe') -Destination (Join-Path $Destination 'deno.exe')
    $denoVersion = $deno.tag_name
}
if ($ExistingFfmpegDirectory -and (Test-Path -LiteralPath (Join-Path $ExistingFfmpegDirectory 'ffmpeg.exe'))) {
    foreach ($name in @('ffmpeg.exe','ffprobe.exe')) { Copy-Item -LiteralPath (Join-Path $ExistingFfmpegDirectory $name) -Destination (Join-Path $Destination $name) }
} else {
    $archive = Join-Path $scratch 'ffmpeg.zip'
    Download 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' $archive
    $checksum = ((Invoke-WebRequest -Uri 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256' -TimeoutSec 60).Content -split '\s+')[0]
    Verify $archive $checksum
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $scratch 'ffmpeg')
    foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
        $binary = Get-ChildItem -LiteralPath (Join-Path $scratch 'ffmpeg') -Filter $name -File -Recurse | Select-Object -First 1
        if (-not $binary) { throw ('No se encontró ' + $name) }
        Copy-Item -LiteralPath $binary.FullName -Destination (Join-Path $Destination $name)
    }
}
$ffVersion = & (Join-Path $Destination 'ffmpeg.exe') -version | Select-Object -First 1
@{ytDlp=$ytVersion;deno=$denoVersion;ffmpeg=$ffVersion} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Destination 'versions.json') -Encoding utf8
# Delete only the verified temporary build directory created by this script.
$destinationRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
if (-not $scratch.StartsWith($destinationRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Ruta temporal fuera del destino.' }
Remove-Item -LiteralPath $scratch -Recurse -Force
