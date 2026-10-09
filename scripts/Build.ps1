param(
    [string]$DotNet = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist\SenjiDownloader'),
    [string]$PreparedEngines = '',
    [switch]$UseExistingFfmpeg,
    [string]$Version = '1.0.0'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versión inválida.' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
& (Join-Path $PSScriptRoot 'New-Icon.ps1')
& $DotNet publish (Join-Path $projectRoot 'SenjiDownloader\SenjiDownloader.csproj') -c Release -r win-x64 --self-contained true -o $output "-p:Version=$Version" --nologo
if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }
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
)) { Invoke-WebRequest -Uri $entry.Url -OutFile (Join-Path $licenses $entry.Name) -TimeoutSec 60 }
$artifacts = Join-Path (Split-Path $output -Parent) 'release'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
Copy-Item -LiteralPath (Join-Path $output 'SenjiDownloader.exe') -Destination (Join-Path $artifacts 'SenjiDownloader.exe')
$hash = (Get-FileHash -LiteralPath (Join-Path $output 'SenjiDownloader.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
($hash + '  SenjiDownloader.exe') | Set-Content -LiteralPath (Join-Path $artifacts 'SenjiDownloader.exe.sha256') -Encoding ascii
$zip = Join-Path $artifacts 'SenjiDownloader-win-x64.zip'
$items = Get-ChildItem -LiteralPath $output | Where-Object Name -ne 'data' | Select-Object -ExpandProperty FullName
Compress-Archive -LiteralPath $items -DestinationPath $zip -CompressionLevel Optimal -Force
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
($zipHash + '  SenjiDownloader-win-x64.zip') | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Output ('Distribución portable lista: ' + $zip)
