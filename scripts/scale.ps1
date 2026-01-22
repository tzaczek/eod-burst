# EOD Burst System - Scale Script (PowerShell)
# Usage: .\scripts\scale.ps1 -Mode [normal|burst|custom] [-FlashPnl 3] [-Regulatory 2]

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("normal", "burst", "custom")]
    [string]$Mode,
    
    [int]$FlashPnl = 1,
    [int]$Regulatory = 1,
    [int]$Ingestion = 1
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  EOD Burst System - Scaling" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

switch ($Mode) {
    "normal" {
        Write-Host "`nScaling to NORMAL mode..." -ForegroundColor Green
        docker compose up -d --scale ingestion=1 --scale flash-pnl=1 --scale regulatory=1
    }
    "burst" {
        Write-Host "`nScaling to BURST mode..." -ForegroundColor Magenta
        Write-Host "  Ingestion:  2 replicas"
        Write-Host "  Flash P&L:  6 replicas (matching Kafka partitions)"
        Write-Host "  Regulatory: 4 replicas"
        docker compose -f docker-compose.yml -f docker-compose.burst.yml up -d
    }
    "custom" {
        Write-Host "`nScaling to CUSTOM configuration..." -ForegroundColor Yellow
        Write-Host "  Ingestion:  $Ingestion replicas"
        Write-Host "  Flash P&L:  $FlashPnl replicas"
        Write-Host "  Regulatory: $Regulatory replicas"
        docker compose up -d --scale ingestion=$Ingestion --scale flash-pnl=$FlashPnl --scale regulatory=$Regulatory
    }
}

Start-Sleep -Seconds 3

Write-Host "`nCurrent service status:" -ForegroundColor Cyan
docker compose ps

Write-Host "`nKafka consumer lag:" -ForegroundColor Cyan
docker compose exec -T kafka kafka-consumer-groups --bootstrap-server localhost:9092 --describe --all-groups 2>$null

Write-Host ""
