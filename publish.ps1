# publish.ps1 - Build and package WorkTools.App for deployment
param(
    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Platform = 'x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$CreateZip
)

$ErrorActionPreference = 'Stop'

# --- Locate MSBuild ---
$msBuildPath = $null
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vsWhere) {
    $msBuildPath = & $vsWhere -latest -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}
if (-not $msBuildPath) {
    $candidates = @(
        "$env:ProgramFiles\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )
    $msBuildPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $msBuildPath) {
    Write-Error "MSBuild not found. Install Visual Studio with the .NET desktop development workload."
    exit 1
}

Write-Host "MSBuild: $msBuildPath" -ForegroundColor Cyan

# --- Paths ---
$solutionDir = $PSScriptRoot
$projectFile = Join-Path $solutionDir "WorkTools.App\WorkTools.App.csproj"
$rid = "win-$Platform"
$publishDir = Join-Path $solutionDir "WorkTools.App\bin\publish\$rid"

# --- Clean previous output ---
if (Test-Path $publishDir) {
    Write-Host "Cleaning previous publish output..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $publishDir
}

# --- Publish ---
Write-Host "`nPublishing WorkTools.App ($Configuration | $Platform)..." -ForegroundColor Green

& $msBuildPath $projectFile `
    /t:Restore /p:Platform=$Platform /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Error "Restore failed."; exit $LASTEXITCODE }

& $msBuildPath $projectFile `
    /t:Publish `
    /p:Configuration=$Configuration `
    /p:Platform=$Platform `
    /p:RuntimeIdentifier=$rid `
    /p:SelfContained=true `
    /p:WindowsAppSDKSelfContained=true `
    /p:PublishDir="bin\publish\$rid\" `
    /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed."; exit $LASTEXITCODE }

Write-Host "`nPublished to: $publishDir" -ForegroundColor Green

# --- Optional zip ---
if ($CreateZip) {
    [xml]$proj = Get-Content $projectFile
    $version = $proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $version) { $version = "1.0.0" }

    $zipName = "WorkTools-v$version-$rid.zip"
    $zipPath = Join-Path $solutionDir $zipName

    if (Test-Path $zipPath) { Remove-Item $zipPath }

    Write-Host "Creating $zipName..." -ForegroundColor Yellow
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath
    Write-Host "Package: $zipPath" -ForegroundColor Green
}

Write-Host "`nDone." -ForegroundColor Green
