# EOD Burst System - Start Script (PowerShell)
# Usage: .\scripts\start.ps1 [-Burst]

param(
    [switch]$Burst,
    [switch]$Build,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  EOD Burst System - Startup Script" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# Clean up if requested
if ($Clean) {
    Write-Host "`nCleaning up previous containers and volumes..." -ForegroundColor Yellow
    docker compose down -v
}

# Build if requested
if ($Build) {
    Write-Host "`nBuilding application images..." -ForegroundColor Yellow
    docker compose build
}

# Start infrastructure first
Write-Host "`nStarting infrastructure services..." -ForegroundColor Green
docker compose up -d zookeeper kafka redis sqlserver minio

# Wait for infrastructure to be healthy
Write-Host "`nWaiting for infrastructure to be ready..." -ForegroundColor Yellow
$maxRetries = 60
$retry = 0

while ($retry -lt $maxRetries) {
    $kafkaHealth = docker inspect --format='{{.State.Health.Status}}' eod-kafka 2>$null
    $redisHealth = docker inspect --format='{{.State.Health.Status}}' eod-redis 2>$null
    $sqlHealth = docker inspect --format='{{.State.Health.Status}}' eod-sqlserver 2>$null
    
    if ($kafkaHealth -eq "healthy" -and $redisHealth -eq "healthy" -and $sqlHealth -eq "healthy") {
        Write-Host "Infrastructure is healthy!" -ForegroundColor Green
        break
    }
    
    Write-Host "  Kafka: $kafkaHealth, Redis: $redisHealth, SQL: $sqlHealth" -ForegroundColor Gray
    Start-Sleep -Seconds 5
    $retry++
}

if ($retry -ge $maxRetries) {
    Write-Host "ERROR: Infrastructure failed to become healthy" -ForegroundColor Red
    exit 1
}

# Start UI tools
Write-Host "`nStarting monitoring UIs..." -ForegroundColor Green
docker compose up -d kafka-ui redis-commander

# Start application services
if ($Burst) {
    Write-Host "`nStarting application services in BURST MODE..." -ForegroundColor Magenta
    docker compose -f docker-compose.yml -f docker-compose.burst.yml up -d ingestion flash-pnl regulatory
} else {
    Write-Host "`nStarting application services in normal mode..." -ForegroundColor Green
    docker compose up -d ingestion flash-pnl regulatory
}

# Wait for apps to start
Start-Sleep -Seconds 5

Write-Host "`n============================================" -ForegroundColor Cyan
Write-Host "  EOD Burst System is running!" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Service URLs:" -ForegroundColor Yellow
Write-Host "  Ingestion Service:     http://localhost:8080/health"
Write-Host "  Flash P&L Service:     http://localhost:8081/health"
Write-Host "  Regulatory Service:    http://localhost:8082/health"
Write-Host ""
Write-Host "Monitoring UIs:" -ForegroundColor Yellow
Write-Host "  Kafka UI:              http://localhost:8090"
Write-Host "  Redis Commander:       http://localhost:8091"
Write-Host "  MinIO Console:         http://localhost:9001 (credentials in .env file)"
Write-Host ""
Write-Host "Useful commands:" -ForegroundColor Yellow
Write-Host "  View logs:             docker compose logs -f [service-name]"
Write-Host "  Scale up:              docker compose up -d --scale flash-pnl=3"
Write-Host "  Stop all:              docker compose down"
Write-Host "  Burst mode:            docker compose -f docker-compose.yml -f docker-compose.burst.yml up -d"
Write-Host ""
