# EOD Burst System - Start with Full Observability
# Usage: .\scripts\start-with-observability.ps1 [-Build] [-Clean]

param(
    [switch]$Build,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  EOD Burst System - Full Observability" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# Clean up if requested
if ($Clean) {
    Write-Host "`nCleaning up previous containers and volumes..." -ForegroundColor Yellow
    docker compose -f docker-compose.yml -f docker-compose.observability.yml down -v
}

# Build if requested
if ($Build) {
    Write-Host "`nBuilding application images..." -ForegroundColor Yellow
    docker compose build
}

# Create network if not exists
docker network create eod-network 2>$null

# Start infrastructure first
Write-Host "`nStarting infrastructure services..." -ForegroundColor Green
docker compose up -d zookeeper kafka redis sqlserver minio

# Wait for core infrastructure
Write-Host "`nWaiting for core infrastructure..." -ForegroundColor Yellow
$maxRetries = 60
$retry = 0

while ($retry -lt $maxRetries) {
    $kafkaHealth = docker inspect --format='{{.State.Health.Status}}' eod-kafka 2>$null
    $redisHealth = docker inspect --format='{{.State.Health.Status}}' eod-redis 2>$null
    
    if ($kafkaHealth -eq "healthy" -and $redisHealth -eq "healthy") {
        Write-Host "Core infrastructure is healthy!" -ForegroundColor Green
        break
    }
    
    Write-Host "  Kafka: $kafkaHealth, Redis: $redisHealth" -ForegroundColor Gray
    Start-Sleep -Seconds 5
    $retry++
}

# Start observability stack
Write-Host "`nStarting observability stack..." -ForegroundColor Magenta
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d `
    jaeger loki promtail otel-collector redis-exporter

# Wait for observability
Write-Host "`nWaiting for observability services..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# Start Grafana
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d grafana

# Start UI tools
Write-Host "`nStarting monitoring UIs..." -ForegroundColor Green
docker compose up -d kafka-ui redis-commander

# Start application services with telemetry
Write-Host "`nStarting application services with telemetry..." -ForegroundColor Green
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d `
    ingestion flash-pnl regulatory

# Wait for apps to start
Start-Sleep -Seconds 10

Write-Host "`n============================================" -ForegroundColor Cyan
Write-Host "  EOD Burst System with Observability" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Application Services:" -ForegroundColor Yellow
Write-Host "  Ingestion:           http://localhost:8080/health"
Write-Host "  Flash P&L:           http://localhost:8081/health"
Write-Host "  Regulatory:          http://localhost:8082/health"
Write-Host ""
Write-Host "Observability Stack:" -ForegroundColor Magenta
Write-Host "  Grafana Dashboards:  http://localhost:3000  (credentials in .env file)"
Write-Host "  Jaeger Tracing:      http://localhost:16686"
Write-Host "  Loki Logs:           http://localhost:3100"
Write-Host "  OTEL Collector:      http://localhost:13133/health"
Write-Host ""
Write-Host "Infrastructure UIs:" -ForegroundColor Yellow
Write-Host "  Kafka UI:            http://localhost:8090"
Write-Host "  Redis Commander:     http://localhost:8091"
Write-Host "  MinIO Console:       http://localhost:9001"
Write-Host ""
Write-Host "Pre-configured Grafana Dashboards:" -ForegroundColor Green
Write-Host "  - EOD Burst - Overview (metrics)"
Write-Host "  - EOD Burst - Tracing (distributed traces)"
Write-Host "  - EOD Burst - Redis (cache performance)"
Write-Host ""
Write-Host "Tips:" -ForegroundColor Cyan
Write-Host "  - Open Grafana and explore the pre-built dashboards"
Write-Host "  - Click on trace IDs to jump to Jaeger for full trace view"
Write-Host "  - Use Loki to search logs with: {container=~\"eod-.*\"}"
Write-Host ""
