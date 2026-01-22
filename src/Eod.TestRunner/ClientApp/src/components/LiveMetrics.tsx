import { TestProgress } from '../types';
import './LiveMetrics.css';

interface LiveMetricsProps {
  progress: TestProgress[];
}

export function LiveMetrics({ progress }: LiveMetricsProps) {
  if (progress.length === 0) return null;
  
  const totalTrades = progress.reduce((sum, p) => sum + p.tradesGenerated, 0);
  const avgThroughput = progress.reduce((sum, p) => sum + p.currentThroughput, 0);
  
  return (
    <div className="live-metrics">
      <div className="metrics-header">
        <span className="pulse-indicator"></span>
        <h3>Live Tests Running</h3>
        <span className="test-count">{progress.length} active</span>
      </div>
      
      <div className="metrics-grid">
        <div className="metric-card">
          <span className="metric-icon">📊</span>
          <div className="metric-content">
            <span className="metric-value">{totalTrades.toLocaleString()}</span>
            <span className="metric-label">Trades Generated</span>
          </div>
        </div>
        
        <div className="metric-card">
          <span className="metric-icon">⚡</span>
          <div className="metric-content">
            <span className="metric-value">{avgThroughput.toFixed(0)}</span>
            <span className="metric-label">Trades/sec</span>
          </div>
        </div>
        
        {progress.map(p => (
          <div key={p.scenarioId} className="metric-card active-test">
            <div className="test-progress-mini">
              <span className="test-name">{p.scenarioId}</span>
              <span className="test-step">{p.currentStep}</span>
              <div className="mini-progress">
                <div 
                  className="mini-progress-fill" 
                  style={{ width: `${p.percentComplete}%` }}
                />
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
