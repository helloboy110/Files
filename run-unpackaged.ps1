# Copyright (c) Files Community
# Licensed under the MIT License.

# Builds the unpackaged (direct-exe) debug build and launches it.
# Usage: .\run-unpackaged.ps1 [-NoLaunch] [-Platform x64]

param(
	[switch]$NoLaunch,
	[ValidateSet('x64', 'x86', 'arm64')]
	[string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (-not $vsPath) {
	Write-Error 'Visual Studio 2026 (v18) with MSBuild was not found.'
	exit 1
}

Import-Module (Join-Path $vsPath 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $vsPath -SkipAutomaticLocation -DevCmdArguments "-arch=$Platform -host_arch=$Platform"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

msbuild -restore src/Files.App/Files.App.csproj `
	-p:Configuration=Debug -p:Platform=$Platform -p:FilesUnpackaged=true `
	-v:quiet -clp:ErrorsOnly -nologo

if ($LASTEXITCODE -ne 0) {
	Write-Error "Build failed with exit code $LASTEXITCODE."
	exit $LASTEXITCODE
}

$exe = Join-Path $repoRoot "src\Files.App\bin\Unpackaged\$Platform\Debug\net10.0-windows10.0.26100.0\win-$Platform\Files.exe"
Write-Host "Build succeeded: $exe"

if (-not $NoLaunch) {
	Start-Process -FilePath $exe
}
