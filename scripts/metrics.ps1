# EOD Burst System - Metrics Script (PowerShell)
# Usage: .\scripts\metrics.ps1

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  EOD Burst System - Metrics Dashboard" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

while ($true) {
    Clear-Host
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  EOD Burst System - Live Metrics" -ForegroundColor Cyan
    Write-Host "  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
    Write-Host "============================================" -ForegroundColor Cyan
    
    # Ingestion metrics
    Write-Host "`n[INGESTION SERVICE]" -ForegroundColor Yellow
    try {
        $ingestion = Invoke-RestMethod -Uri "http://localhost:8080/metrics" -TimeoutSec 2
        Write-Host "  Messages Sent:    $($ingestion.messagesSent)"
        Write-Host "  Delivery Errors:  $($ingestion.deliveryErrors)"
    } catch {
        Write-Host "  Service unavailable" -ForegroundColor Red
    }
    
    # Flash P&L metrics
    Write-Host "`n[FLASH P&L SERVICE]" -ForegroundColor Yellow
    try {
        $pnl = Invoke-RestMethod -Uri "http://localhost:8081/metrics" -TimeoutSec 2
        Write-Host "  Messages Consumed: $($pnl.messagesConsumed)"
        Write-Host "  Consumer Lag:      $($pnl.consumerLag)"
        Write-Host "  Unique Positions:  $($pnl.uniquePositions)"
        Write-Host "  Unique Traders:    $($pnl.uniqueTraders)"
    } catch {
        Write-Host "  Service unavailable" -ForegroundColor Red
    }
    
    # Regulatory metrics
    Write-Host "`n[REGULATORY SERVICE]" -ForegroundColor Yellow
    try {
        $reg = Invoke-RestMethod -Uri "http://localhost:8082/metrics" -TimeoutSec 2
        Write-Host "  Messages Consumed: $($reg.messagesConsumed)"
        Write-Host "  Consumer Lag:      $($reg.consumerLag)"
        Write-Host "  Trades Inserted:   $($reg.tradesInserted)"
        Write-Host "  Insert Errors:     $($reg.insertErrors)"
        Write-Host "  Batches Processed: $($reg.batchesProcessed)"
    } catch {
        Write-Host "  Service unavailable" -ForegroundColor Red
    }
    
    # Container status
    Write-Host "`n[CONTAINER STATUS]" -ForegroundColor Yellow
    docker compose ps --format "table {{.Name}}\t{{.Status}}\t{{.Ports}}" 2>$null
    
    Write-Host "`nPress Ctrl+C to exit. Refreshing in 5 seconds..." -ForegroundColor Gray
    Start-Sleep -Seconds 5
}
