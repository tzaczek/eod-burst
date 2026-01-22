#!/bin/bash
# EOD Burst System - Codespaces Startup Script

echo "🚀 Starting EOD Burst System..."
echo ""

# Start all containers
docker compose up -d

echo ""
echo "⏳ Waiting for services to be ready..."
echo ""

# Wait for Test Dashboard to be healthy (max 3 minutes)
MAX_RETRIES=36
RETRY_COUNT=0

while [ $RETRY_COUNT -lt $MAX_RETRIES ]; do
    if curl -s -o /dev/null -w "%{http_code}" http://localhost:8083/health | grep -q "200"; then
        echo ""
        echo "✅ Test Dashboard is ready!"
        echo ""
        echo "═══════════════════════════════════════════════════════════════"
        echo "  🎉 EOD Burst System is running!"
        echo "═══════════════════════════════════════════════════════════════"
        echo ""
        echo "  📍 Access the services from the PORTS tab below:"
        echo ""
        echo "     🧪 Test Dashboard    → Port 8083 (click the 🌐 globe icon)"
        echo "     📊 Grafana           → Port 3000"
        echo "     🔍 Jaeger Tracing    → Port 16686"
        echo "     📨 Kafka UI          → Port 8090"
        echo "     📦 MinIO Console     → Port 9001"
        echo ""
        echo "═══════════════════════════════════════════════════════════════"
        echo ""
        exit 0
    fi
    
    RETRY_COUNT=$((RETRY_COUNT + 1))
    echo "  Waiting for services... ($RETRY_COUNT/$MAX_RETRIES)"
    sleep 5
done

echo ""
echo "⚠️  Services taking longer than expected. Check with:"
echo "    docker compose ps"
echo "    docker compose logs -f"
echo ""
