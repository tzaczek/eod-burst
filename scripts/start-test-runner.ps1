# Start Test Runner with full stack
# Starts infrastructure, application services, and test runner

param(
    [switch]$Build = $false,
    [switch]$Observability = $false,
    [switch]$DevMode = $false
)

$ErrorActionPreference = "Stop"

Write-Host "======================================" -ForegroundColor Cyan
Write-Host " EOD Burst - Test Runner Startup" -ForegroundColor Cyan  
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Build React frontend if needed
$wwwrootPath = Join-Path $PSScriptRoot "..\src\Eod.TestRunner\wwwroot"
if (-not (Test-Path (Join-Path $wwwrootPath "index.html"))) {
    Write-Host "[Pre-build] Building React dashboard..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "build-test-dashboard.ps1")
}

# Start services
Write-Host "[1/3] Starting infrastructure services..." -ForegroundColor Yellow
docker compose up -d zookeeper kafka redis sqlserver minio

Write-Host "[2/3] Waiting for services to be healthy..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# Build if requested
$buildFlag = ""
if ($Build) {
    $buildFlag = "--build"
    Write-Host "       Building images..." -ForegroundColor Yellow
}

Write-Host "[3/3] Starting application services..." -ForegroundColor Yellow

if ($Observability) {
    docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d $buildFlag ingestion flash-pnl regulatory test-runner
} else {
    docker compose up -d $buildFlag ingestion flash-pnl regulatory test-runner
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Green
Write-Host " Services Started!" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Test Dashboard:   http://localhost:8083" -ForegroundColor Cyan
Write-Host "  Test API Docs:    http://localhost:8083/swagger" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Kafka UI:         http://localhost:8090" -ForegroundColor White
Write-Host "  Redis Commander:  http://localhost:8091" -ForegroundColor White
Write-Host "  MinIO Console:    http://localhost:9001" -ForegroundColor White

if ($Observability) {
    Write-Host ""
    Write-Host "  Grafana:          http://localhost:3000" -ForegroundColor Magenta
    Write-Host "  Jaeger UI:        http://localhost:16686" -ForegroundColor Magenta
}

Write-Host ""
Write-Host "Run 'docker compose logs -f test-runner' to view logs" -ForegroundColor Gray
Write-Host ""

if ($DevMode) {
    Write-Host "Starting frontend dev server..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "build-test-dashboard.ps1") -Watch
}
