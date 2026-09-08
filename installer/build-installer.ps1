<#
.SYNOPSIS
  Builds the Windows installer: publish -> trim -> compile with Inno Setup.

.DESCRIPTION
  Publishes SELF-CONTAINED, so the installed app needs no .NET runtime on the target machine.
  That costs about 160 MB of framework before compression, and is the whole point: a user
  double-clicks the setup and the app runs, with no prerequisite to hunt down. ffmpeg is still
  fetched on first run by the app itself.

.PARAMETER Version
  Version stamped into the installer and its filename. Defaults to the <Version> in the app
  .csproj so the two cannot drift apart.

.PARAMETER SkipPublish
  Reuse an existing artifacts\publish folder. Only for iterating on the .iss - a stale publish
  will happily package yesterday's code.

.EXAMPLE
  .\build-installer.ps1
  .\build-installer.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
  [string]$Version,
  [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repo       = Split-Path $PSScriptRoot -Parent
$appProj    = Join-Path $repo 'src\WhisperSubtitleGenerator.App\WhisperSubtitleGenerator.App.csproj'
$publishDir = Join-Path $repo 'artifacts\publish'
$outputDir  = Join-Path $repo 'artifacts\installer'

# ---- version -------------------------------------------------------------------------------
if (-not $Version) {
  $Version = ([xml](Get-Content $appProj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
  if (-not $Version) { throw "No <Version> in $appProj and none passed with -Version." }
}
Write-Host "  version: $Version"

# ---- find the Inno Setup compiler ------------------------------------------------------------
$iscc = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  $onPath = Get-Command iscc -ErrorAction SilentlyContinue
  if ($onPath) { $iscc = $onPath.Source }
}
if (-not $iscc) {
  throw "Inno Setup not found. Install it with:  winget install JRSoftware.InnoSetup"
}
Write-Host "  compiler: $iscc"

# ---- publish -------------------------------------------------------------------------------
if (-not $SkipPublish) {
  if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
  Write-Host "  publishing self-contained win-x64 ..."

  & dotnet publish $appProj -c Release -r win-x64 --self-contained true `
      -p:PublishSingleFile=false -p:DebugType=none -p:Version=$Version `
      -o $publishDir --nologo | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

  # Whisper.net.Runtime ships native binaries for every platform it supports. On a Windows x64
  # installer the Linux, macOS, win-x86 and win-arm64 copies are dead weight - drop them.
  # win-x64 is KEPT and must stay: without it the app starts fine and then throws on the first
  # transcription with a missing-native-library error.
  $runtimes = Join-Path $publishDir 'runtimes'
  if (Test-Path $runtimes) {
    $before = (Get-ChildItem $runtimes -Recurse -File | Measure-Object Length -Sum).Sum
    Get-ChildItem $runtimes -Directory |
      Where-Object { $_.Name -ne 'win-x64' } |
      ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
    $after = (Get-ChildItem $runtimes -Recurse -File | Measure-Object Length -Sum).Sum
    "  trimmed non-Windows runtimes: {0:N1} MB -> {1:N1} MB" -f ($before/1MB), ($after/1MB) | Write-Host

    if (-not (Test-Path (Join-Path $runtimes 'win-x64'))) {
      throw "runtimes\win-x64 is missing after trimming - the app would fail at transcription time."
    }
  }
}

if (-not (Test-Path (Join-Path $publishDir 'WhisperSubtitleGenerator.exe'))) {
  throw "No published exe in $publishDir. Run without -SkipPublish."
}
$payload = (Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum
"  payload: {0:N0} files, {1:N1} MB" -f (Get-ChildItem $publishDir -Recurse -File).Count, ($payload/1MB) | Write-Host

# ---- compile the installer -------------------------------------------------------------------
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
Write-Host "  compiling installer ..."

& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'WhisperSubtitleGenerator.iss') | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

$setup = Get-ChildItem $outputDir -Filter '*.exe' | Sort-Object LastWriteTime | Select-Object -Last 1
Write-Host ''
"  BUILT: {0}" -f $setup.FullName | Write-Host
"         {0:N1} MB  (payload was {1:N1} MB, so ~{2:N0}% compression)" -f `
  ($setup.Length/1MB), ($payload/1MB), (100 - ($setup.Length / $payload * 100)) | Write-Host
Write-Host ''
