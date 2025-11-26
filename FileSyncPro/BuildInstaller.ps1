# PowerShell Script to Build FileSyncPro Installer
# This script builds the application and creates the installer package

param(
    [string]$Configuration = "Release"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Petrofac FileSyncPro Installer Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

# Step 1: Clean previous builds
Write-Host "[1/5] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path ".\bin\$Configuration") {
    Remove-Item -Path ".\bin\$Configuration" -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path ".\obj") {
    Remove-Item -Path ".\obj" -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path ".\Installer") {
    Remove-Item -Path ".\Installer" -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "   Cleanup completed." -ForegroundColor Green
Write-Host ""

# Step 2: Restore NuGet packages
Write-Host "[2/5] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore "FileSyncPro.csproj"
if ($LASTEXITCODE -ne 0) {
    Write-Host "   ERROR: NuGet restore failed!" -ForegroundColor Red
    exit 1
}
Write-Host "   Packages restored successfully." -ForegroundColor Green
Write-Host ""

# Step 3: Build the application
Write-Host "[3/5] Building application in $Configuration mode..." -ForegroundColor Yellow
dotnet build "FileSyncPro.csproj" --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "   ERROR: Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "   Build completed successfully." -ForegroundColor Green
Write-Host ""

# Step 4: Check for Inno Setup
Write-Host "[4/5] Checking for Inno Setup..." -ForegroundColor Yellow
$InnoSetupPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
    "C:\Program Files\Inno Setup 5\ISCC.exe"
)

$InnoSetupExe = $null
foreach ($path in $InnoSetupPaths) {
    if (Test-Path $path) {
        $InnoSetupExe = $path
        break
    }
}

if ($null -eq $InnoSetupExe) {
    Write-Host "   WARNING: Inno Setup not found!" -ForegroundColor Red
    Write-Host "   Please install Inno Setup from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "   Alternative: Use the publish output to create a ZIP package" -ForegroundColor Yellow
    Write-Host ""

    # Create a simple ZIP package as fallback
    Write-Host "[5/5] Creating ZIP package..." -ForegroundColor Yellow
    $ZipPath = ".\Installer\PetrofacFileSyncPro_Portable_v1.0.0.zip"
    New-Item -Path ".\Installer" -ItemType Directory -Force | Out-Null

    Compress-Archive -Path ".\bin\$Configuration\net6.0-windows\*" -DestinationPath $ZipPath -Force

    Write-Host "   ZIP package created: $ZipPath" -ForegroundColor Green
    Write-Host ""
    Write-Host "Portable package created successfully!" -ForegroundColor Cyan
    Write-Host "Location: $(Resolve-Path $ZipPath)" -ForegroundColor Cyan
    exit 0
}

Write-Host "   Found Inno Setup at: $InnoSetupExe" -ForegroundColor Green
Write-Host ""

# Step 5: Build the installer
Write-Host "[5/5] Building installer package..." -ForegroundColor Yellow
& $InnoSetupExe "setup.iss"
if ($LASTEXITCODE -ne 0) {
    Write-Host "   ERROR: Installer build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "   Installer created successfully." -ForegroundColor Green
Write-Host ""

# Display results
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BUILD COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$InstallerPath = Get-ChildItem -Path ".\Installer" -Filter "*.exe" | Select-Object -First 1
if ($InstallerPath) {
    Write-Host "Installer Location:" -ForegroundColor Yellow
    Write-Host "   $(Resolve-Path $InstallerPath.FullName)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Installer Size: $([math]::Round($InstallerPath.Length / 1MB, 2)) MB" -ForegroundColor Yellow
}
Write-Host ""
