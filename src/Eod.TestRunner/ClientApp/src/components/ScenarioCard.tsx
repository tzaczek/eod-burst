import { TestScenario, TestProgress, TestType } from '../types';
import './ScenarioCard.css';

interface ScenarioCardProps {
  scenario: TestScenario;
  progress?: TestProgress;
  onRun: () => void;
  onCancel: () => void;
  style?: React.CSSProperties;
}

const typeIcons: Record<TestType, string> = {
  HealthCheck: '🏥',
  Throughput: '🚀',
  Latency: '⏱️',
  EndToEnd: '🔄',
  BurstMode: '💥',
  DataIntegrity: '🔒'
};

const typeColors: Record<TestType, string> = {
  HealthCheck: 'var(--accent-green)',
  Throughput: 'var(--accent-cyan)',
  Latency: 'var(--accent-yellow)',
  EndToEnd: 'var(--accent-magenta)',
  BurstMode: 'var(--accent-orange)',
  DataIntegrity: 'var(--status-passed)'
};

export function ScenarioCard({ scenario, progress, onRun, onCancel, style }: ScenarioCardProps) {
  const isRunning = !!progress;
  const typeColor = typeColors[scenario.type];
  
  return (
    <div 
      className={`scenario-card animate-in ${isRunning ? 'running' : ''}`}
      style={{ ...style, '--type-color': typeColor } as React.CSSProperties}
    >
      <div className="card-header">
        <span className="type-icon">{typeIcons[scenario.type]}</span>
        <span className="type-badge">{scenario.type}</span>
      </div>
      
      <h3 className="scenario-name">{scenario.name}</h3>
      <p className="scenario-description">{scenario.description}</p>
      
      <div className="parameters-preview">
        {scenario.parameters.tradeCount > 0 && (
          <div className="param">
            <span className="param-label">Trades</span>
            <span className="param-value">{scenario.parameters.tradeCount}</span>
          </div>
        )}
        {scenario.parameters.tradesPerSecond > 0 && (
          <div className="param">
            <span className="param-label">Rate</span>
            <span className="param-value">{scenario.parameters.tradesPerSecond}/s</span>
          </div>
        )}
        {scenario.type === 'BurstMode' && (
          <div className="param">
            <span className="param-label">Burst</span>
            <span className="param-value">{scenario.parameters.burstMultiplier}x</span>
          </div>
        )}
        {scenario.type === 'Latency' && (
          <div className="param">
            <span className="param-label">SLA</span>
            <span className="param-value">{scenario.parameters.expectedLatencyMs}ms</span>
          </div>
        )}
      </div>
      
      {isRunning && (
        <div className="progress-section">
          <div className="progress-bar">
            <div 
              className="progress-fill" 
              style={{ width: `${progress.percentComplete}%` }}
            />
          </div>
          <div className="progress-info">
            <span className="progress-step">{progress.currentStep}</span>
            <span className="progress-percent">{progress.percentComplete}%</span>
          </div>
          {progress.currentThroughput > 0 && (
            <div className="live-stats">
              <span className="stat">
                <span className="stat-value">{progress.tradesGenerated}</span> trades
              </span>
              <span className="stat">
                <span className="stat-value">{progress.currentThroughput.toFixed(0)}</span>/sec
              </span>
            </div>
          )}
        </div>
      )}
      
      <div className="card-actions">
        {isRunning ? (
          <button className="btn btn-cancel" onClick={onCancel}>
            <span className="btn-icon">✕</span>
            Cancel
          </button>
        ) : (
          <button className="btn btn-run" onClick={onRun}>
            <span className="btn-icon">▶</span>
            Run Test
          </button>
        )}
      </div>
    </div>
  );
}
