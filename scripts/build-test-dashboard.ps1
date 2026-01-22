# Build Test Dashboard React App
# This script builds the React frontend and copies it to wwwroot

param(
    [switch]$Watch = $false
)

$ErrorActionPreference = "Stop"

Write-Host "======================================" -ForegroundColor Cyan
Write-Host " Building EOD Test Dashboard" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

$clientAppPath = Join-Path $PSScriptRoot "..\src\Eod.TestRunner\ClientApp"
$wwwrootPath = Join-Path $PSScriptRoot "..\src\Eod.TestRunner\wwwroot"

# Navigate to ClientApp
Push-Location $clientAppPath

try {
    # Check if node_modules exists
    if (-not (Test-Path "node_modules")) {
        Write-Host "[1/3] Installing dependencies..." -ForegroundColor Yellow
        npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
    } else {
        Write-Host "[1/3] Dependencies already installed" -ForegroundColor Green
    }

    if ($Watch) {
        Write-Host "[2/3] Starting development server..." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  Frontend:  http://localhost:5173" -ForegroundColor Cyan
        Write-Host "  API Proxy: http://localhost:8083" -ForegroundColor Cyan
        Write-Host ""
        npm run dev
    } else {
        Write-Host "[2/3] Building for production..." -ForegroundColor Yellow
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm build failed" }

        Write-Host "[3/3] Build complete!" -ForegroundColor Green
        Write-Host ""
        Write-Host "  Output: $wwwrootPath" -ForegroundColor Cyan
        Write-Host ""
    }
}
finally {
    Pop-Location
}

Write-Host "Done!" -ForegroundColor Green
