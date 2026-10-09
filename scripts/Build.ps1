param(
    [string]$DotNet = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist\SenjiDownloader'),
    [string]$PreparedEngines = '',
    [string]$PreparedLicenses = '',
    [switch]$UseExistingFfmpeg,
    [string]$Version = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $Version) { [xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'SenjiDownloader\SenjiDownloader.csproj'); $Version = $project.Project.PropertyGroup.Version }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versión inválida.' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
& (Join-Path $PSScriptRoot 'New-Icon.ps1')
$toolParams = @{Destination=(Join-Path $output 'tools');PreparedEngines=$PreparedEngines}
if ($UseExistingFfmpeg) { $toolParams.ExistingFfmpegDirectory = Join-Path $projectRoot 'bin' }
& (Join-Path $PSScriptRoot 'Get-Tools.ps1') @toolParams
Copy-Item -LiteralPath (Join-Path $projectRoot 'LEEME.txt'),(Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'SenjiDownloader\assets\senji.ico') -Destination $output
$licenses = Join-Path $output 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
foreach ($entry in @(
    @{Name='yt-dlp.txt';Url='https://raw.githubusercontent.com/yt-dlp/yt-dlp/master/LICENSE'},
    @{Name='FFmpeg-GPLv3.txt';Url='https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/COPYING.GPLv3'},
    @{Name='Deno.txt';Url='https://raw.githubusercontent.com/denoland/deno/main/LICENSE.md'},
    @{Name='dotnet.txt';Url='https://raw.githubusercontent.com/dotnet/runtime/main/LICENSE.TXT'},
    @{Name='dotnet-third-party.txt';Url='https://raw.githubusercontent.com/dotnet/runtime/main/THIRD-PARTY-NOTICES.TXT'},
    @{Name='yt-dlp-third-party.txt';Url='https://raw.githubusercontent.com/yt-dlp/yt-dlp/master/THIRD_PARTY_LICENSES.txt'}
)) {
    $prepared = if ($PreparedLicenses) { Join-Path $PreparedLicenses $entry.Name } else { '' }
    if ($prepared -and (Test-Path -LiteralPath $prepared)) { Copy-Item -LiteralPath $prepared -Destination (Join-Path $licenses $entry.Name) }
    else { Invoke-WebRequest -Uri $entry.Url -OutFile (Join-Path $licenses $entry.Name) -TimeoutSec 60 }
}
# The standalone EXE carries its tools; they are unpacked only after the UI appears.
Copy-Item -LiteralPath $licenses -Destination (Join-Path $output 'tools') -Recurse -Force
$bundle = Join-Path $projectRoot 'SenjiDownloader\assets\tools.zip'
$bundleItems = Get-ChildItem -LiteralPath (Join-Path $output 'tools') | Where-Object Name -in @('yt-dlp.exe','ffmpeg.exe','ffprobe.exe','deno.exe','versions.json','licenses') | Select-Object -ExpandProperty FullName
Compress-Archive -LiteralPath $bundleItems -DestinationPath $bundle -CompressionLevel Optimal -Force
& $DotNet publish (Join-Path $projectRoot 'SenjiDownloader\SenjiDownloader.csproj') -c Release -r win-x64 --self-contained true -o $output "-p:Version=$Version" --nologo
if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }
$artifacts = Join-Path (Split-Path $output -Parent) 'release'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
Copy-Item -LiteralPath (Join-Path $output 'SenjiDownloader.exe') -Destination (Join-Path $artifacts 'SenjiDownloader.exe')
$hash = (Get-FileHash -LiteralPath (Join-Path $output 'SenjiDownloader.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
($hash + '  SenjiDownloader.exe') | Set-Content -LiteralPath (Join-Path $artifacts 'SenjiDownloader.exe.sha256') -Encoding ascii
$zip = Join-Path $artifacts 'SenjiDownloader-win-x64.zip'
$items = Get-ChildItem -LiteralPath $output | Where-Object Name -notin @('data','tools') | Select-Object -ExpandProperty FullName
Compress-Archive -LiteralPath $items -DestinationPath $zip -CompressionLevel Optimal -Force
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
($zipHash + '  SenjiDownloader-win-x64.zip') | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Output ('Distribución portable lista: ' + $zip)
