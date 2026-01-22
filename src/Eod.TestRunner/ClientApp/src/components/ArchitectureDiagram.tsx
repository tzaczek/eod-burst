import { useEffect, useState } from 'react';
import './ArchitectureDiagram.css';

interface SystemMetrics {
  ingestion: {
    tradesIngested: number;
    messagesPerSecond: number;
    status: 'up' | 'down';
  };
  kafka: {
    messagesInTopic: number;
    consumerLag: number;
    status: 'up' | 'down';
  };
  flashPnl: {
    tradesProcessed: number;
    positionsInRedis: number;
    status: 'up' | 'down';
  };
  regulatory: {
    tradesInserted: number;
    batchesPending: number;
    status: 'up' | 'down';
  };
  redis: {
    connectedClients: number;
    keysCount: number;
    status: 'up' | 'down';
  };
  sqlServer: {
    totalTrades: number;
    status: 'up' | 'down';
  };
}

export function ArchitectureDiagram() {
  const [metrics, setMetrics] = useState<SystemMetrics | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchMetrics = async () => {
      try {
        const response = await fetch('/api/scenarios/metrics');
        if (response.ok) {
          const data = await response.json();
          setMetrics(data);
        }
      } catch (error) {
        console.error('Failed to fetch metrics:', error);
      } finally {
        setLoading(false);
      }
    };

    fetchMetrics();
    const interval = setInterval(fetchMetrics, 5000);
    return () => clearInterval(interval);
  }, []);

  const formatNumber = (num: number): string => {
    if (num >= 1000000) return `${(num / 1000000).toFixed(1)}M`;
    if (num >= 1000) return `${(num / 1000).toFixed(1)}K`;
    return num.toString();
  };

  if (loading) {
    return (
      <div className="architecture-diagram loading">
        <div className="loading-spinner"></div>
        <span>Loading system metrics...</span>
      </div>
    );
  }

  return (
    <div className="architecture-diagram">
      <h3 className="diagram-title">
        <span className="title-icon">🏗️</span>
        System Architecture
        <span className="live-indicator">
          <span className="live-dot"></span>
          LIVE
        </span>
      </h3>
      
      <div className="diagram-container">
        {/* Data Source */}
        <div className="component source">
          <div className="component-icon">📡</div>
          <div className="component-name">FIX Simulator</div>
          <div className="component-subtitle">Trade Generator</div>
        </div>

        <div className="flow-arrow">
          <div className="arrow-line"></div>
          <div className="arrow-head">▶</div>
        </div>

        {/* Ingestion Service */}
        <div className={`component service ${metrics?.ingestion.status || 'down'}`}>
          <div className="component-icon">🔄</div>
          <div className="component-name">Ingestion</div>
          <div className="component-metrics">
            <div className="metric">
              <span className="metric-value">{formatNumber(metrics?.ingestion.tradesIngested || 0)}</span>
              <span className="metric-label">trades</span>
            </div>
            <div className="metric">
              <span className="metric-value">{metrics?.ingestion.messagesPerSecond || 0}</span>
              <span className="metric-label">/sec</span>
            </div>
          </div>
          <div className={`status-badge ${metrics?.ingestion.status || 'down'}`}>
            {metrics?.ingestion.status === 'up' ? '●' : '○'}
          </div>
        </div>

        <div className="flow-arrow">
          <div className="arrow-line"></div>
          <div className="arrow-head">▶</div>
        </div>

        {/* Kafka */}
        <div className={`component infrastructure ${metrics?.kafka.status || 'down'}`}>
          <div className="component-icon">📨</div>
          <div className="component-name">Kafka</div>
          <div className="component-subtitle">trades.raw</div>
          <div className="component-metrics">
            <div className="metric">
              <span className="metric-value">{formatNumber(metrics?.kafka.messagesInTopic || 0)}</span>
              <span className="metric-label">msgs</span>
            </div>
            <div className="metric lag">
              <span className="metric-value">{formatNumber(metrics?.kafka.consumerLag || 0)}</span>
              <span className="metric-label">lag</span>
            </div>
          </div>
          <div className={`status-badge ${metrics?.kafka.status || 'down'}`}>
            {metrics?.kafka.status === 'up' ? '●' : '○'}
          </div>
        </div>

        {/* Split into two paths */}
        <div className="flow-split">
          {/* Hot Path */}
          <div className="path hot-path">
            <div className="path-label">
              <span className="path-icon">🔥</span>
              HOT PATH
            </div>
            
            <div className="flow-arrow vertical">
              <div className="arrow-line"></div>
              <div className="arrow-head">▼</div>
            </div>

            <div className={`component service ${metrics?.flashPnl.status || 'down'}`}>
              <div className="component-icon">⚡</div>
              <div className="component-name">Flash P&L</div>
              <div className="component-metrics">
                <div className="metric">
                  <span className="metric-value">{formatNumber(metrics?.flashPnl.tradesProcessed || 0)}</span>
                  <span className="metric-label">processed</span>
                </div>
              </div>
              <div className={`status-badge ${metrics?.flashPnl.status || 'down'}`}>
                {metrics?.flashPnl.status === 'up' ? '●' : '○'}
              </div>
            </div>

            <div className="flow-arrow vertical">
              <div className="arrow-line"></div>
              <div className="arrow-head">▼</div>
            </div>

            <div className={`component storage ${metrics?.redis.status || 'down'}`}>
              <div className="component-icon">💾</div>
              <div className="component-name">Redis</div>
              <div className="component-metrics">
                <div className="metric">
                  <span className="metric-value">{formatNumber(metrics?.flashPnl.positionsInRedis || 0)}</span>
                  <span className="metric-label">positions</span>
                </div>
                <div className="metric">
                  <span className="metric-value">{metrics?.redis.connectedClients || 0}</span>
                  <span className="metric-label">clients</span>
                </div>
              </div>
              <div className={`status-badge ${metrics?.redis.status || 'down'}`}>
                {metrics?.redis.status === 'up' ? '●' : '○'}
              </div>
            </div>
          </div>

          {/* Cold Path */}
          <div className="path cold-path">
            <div className="path-label">
              <span className="path-icon">❄️</span>
              COLD PATH
            </div>

            <div className="flow-arrow vertical">
              <div className="arrow-line"></div>
              <div className="arrow-head">▼</div>
            </div>

            <div className={`component service ${metrics?.regulatory.status || 'down'}`}>
              <div className="component-icon">📋</div>
              <div className="component-name">Regulatory</div>
              <div className="component-metrics">
                <div className="metric">
                  <span className="metric-value">{formatNumber(metrics?.regulatory.tradesInserted || 0)}</span>
                  <span className="metric-label">inserted</span>
                </div>
              </div>
              <div className={`status-badge ${metrics?.regulatory.status || 'down'}`}>
                {metrics?.regulatory.status === 'up' ? '●' : '○'}
              </div>
            </div>

            <div className="flow-arrow vertical">
              <div className="arrow-line"></div>
              <div className="arrow-head">▼</div>
            </div>

            <div className={`component storage ${metrics?.sqlServer.status || 'down'}`}>
              <div className="component-icon">🗄️</div>
              <div className="component-name">SQL Server</div>
              <div className="component-metrics">
                <div className="metric">
                  <span className="metric-value">{formatNumber(metrics?.sqlServer.totalTrades || 0)}</span>
                  <span className="metric-label">total trades</span>
                </div>
              </div>
              <div className={`status-badge ${metrics?.sqlServer.status || 'down'}`}>
                {metrics?.sqlServer.status === 'up' ? '●' : '○'}
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="diagram-legend">
        <div className="legend-item">
          <span className="legend-dot up"></span>
          <span>Healthy</span>
        </div>
        <div className="legend-item">
          <span className="legend-dot down"></span>
          <span>Unavailable</span>
        </div>
        <div className="legend-item">
          <span className="legend-icon">🔥</span>
          <span>Low Latency</span>
        </div>
        <div className="legend-item">
          <span className="legend-icon">❄️</span>
          <span>High Throughput</span>
        </div>
      </div>
    </div>
  );
}
