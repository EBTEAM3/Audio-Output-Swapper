<#
.SYNOPSIS
Builds the release executable.

.DESCRIPTION
Produces the same single-file exe the GitHub releases ship. Self-contained by
default, so the result runs on any Windows 10 19041+ machine with no
prerequisites at all -- which is the whole point for something people download
and double-click.

.PARAMETER SelfContained
Bundle the .NET runtime (default). Pass -SelfContained:$false for a much smaller
exe that requires the .NET 10 Desktop Runtime on the target machine.

.PARAMETER Output
Where to put the exe. Defaults to dist/ next to this script.

.EXAMPLE
.\build.ps1

.EXAMPLE
.\build.ps1 -SelfContained:$false -Output C:\tools
#>
[CmdletBinding()]
param(
    [switch]$SelfContained = $true,
    # Resolved in the body, not here: $PSScriptRoot is not reliably populated
    # inside a param() default under Windows PowerShell 5.1.
    [string]$Output
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Output) { $Output = Join-Path $root 'dist' }

$project = Join-Path $root 'src/AudioSwapper/AudioSwapper.csproj'
if (-not (Test-Path $project)) { throw "Cannot find $project" }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0'
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null

# Note: not $args -- that is a PowerShell automatic variable and assigning to it
# inside a script quietly breaks argument handling.
$standalone = [bool]$SelfContained

$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    $(if ($standalone) { '--self-contained' } else { '--no-self-contained' }),
    '-p:PublishSingleFile=true',
    # WebView2 ships a native loader that cannot live inside the bundle; this
    # extracts it beside the exe at runtime rather than requiring a loose DLL.
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none',
    '-o', $Output
)

if ($standalone) {
    # Roughly halves the download. Costs a little startup time on first run.
    $publishArgs += '-p:EnableCompressionInSingleFile=true'
}

Write-Host "Building $(if ($standalone) { 'self-contained' } else { 'framework-dependent' })..." -ForegroundColor Cyan
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# The publish drops XML doc files for the WebView2 package next to the exe. They
# are not needed to run and only confuse someone looking at the output folder.
Get-ChildItem -Path $Output -Filter *.xml | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $Output -Filter *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Output 'AudioSwapper.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe but it was not produced" }

$mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "  $exe" -ForegroundColor Green
Write-Host "  $mb MB$(if (-not $standalone) { '  (requires the .NET 10 Desktop Runtime)' })"
