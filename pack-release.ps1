# Packs a clean release zip from the unpackaged (direct-exe) build output.
# Usage: .\pack-release.ps1 [-Platform x64] [-Configuration Release]
# Excludes debug symbols, package archives and any local config/log files.

param(
	[ValidateSet('x64', 'x86', 'arm64')]
	[string]$Platform = 'x64',
	[string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$out = "src\Files.App\bin\Unpackaged\$Platform\$Configuration\net10.0-windows10.0.26100.0\win-$Platform"
if (-not (Test-Path "$out\Files.exe")) {
	Write-Error "Build output not found: $out. Build first: msbuild -p:Configuration=$Configuration -p:Platform=$Platform -p:FilesUnpackaged=true"
	exit 1
}

$version = (Get-Item "$out\Files.exe").VersionInfo.ProductVersion -replace '\+.*$', ''
$zip = "artifacts\Files-$version-$Platform.zip"
New-Item -ItemType Directory -Force artifacts | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }

# Never ship: debug symbols, nuget archives, local configs/logs, editor junk
$excludeFiles = @(
	'*.pdb', '*.nupkg',
	'*.config', '*.log', '*.user', '*.suo', '*.bak',
	'launchSettings.json', 'Directory.Build.props.user'
)

$stage = Join-Path $env:TEMP ("Files-pack-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
robocopy $out $stage /E /XF $excludeFiles /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
	Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
	Write-Error "robocopy failed with exit code $LASTEXITCODE"
	exit $LASTEXITCODE
}

Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

$z = Get-Item $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "Packed: $zip ($([math]::Round($z.Length / 1MB, 1)) MB)"
Write-Host "SHA256: $hash"
