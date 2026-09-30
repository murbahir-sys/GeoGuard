# Builds the self-contained app and the installer:
#   powershell -ExecutionPolicy Bypass -File build\build.ps1 [-RunTests]
# Result: dist\installer\GeoGuard-Setup-<version>.exe
# Needs: .NET SDK 10 and Inno Setup 6 (winget install JRSoftware.InnoSetup).
param([switch]$RunTests)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "GeoGuard\GeoGuard.csproj"
$appDir = Join-Path $root "dist\app"
$installerDir = Join-Path $root "dist\installer"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup" }

[xml]$xml = Get-Content $project -Raw
$version = ($xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "GeoGuard $version"

if ($RunTests) {
    Write-Host "== tests =="
    & $dotnet build (Join-Path $root "GeoGuard.Tests") --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "test project build failed" }
    $victim = Join-Path $root "GeoGuard.TestVictim\bin\Debug\net10.0\geoguard_testproc.exe"
    & (Join-Path $root "GeoGuard.Tests\bin\Debug\net10.0-windows\GeoGuard.Tests.exe") $victim
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

Write-Host "== publish (self-contained) =="
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
& $dotnet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o $appDir --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Write-Host "== installer =="
& $iscc "/DAppVersion=$version" "/DSourceDir=$appDir" "/DOutputDir=$installerDir" (Join-Path $root "installer\GeoGuard.iss")
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }

$setup = Join-Path $installerDir "GeoGuard-Setup-$version.exe"
Write-Host ("Done: {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
Write-Host ("SHA256: " + (Get-FileHash $setup -Algorithm SHA256).Hash)
