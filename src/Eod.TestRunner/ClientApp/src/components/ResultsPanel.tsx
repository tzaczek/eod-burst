import { useState } from 'react';
import { TestResult, TestProgress, TestStatus } from '../types';
import './ResultsPanel.css';

interface ResultsPanelProps {
  results: TestResult[];
  runningProgress: TestProgress[];
}

const statusColors: Record<TestStatus, string> = {
  Passed: 'var(--status-passed)',
  Failed: 'var(--status-failed)',
  Running: 'var(--status-running)',
  Pending: 'var(--status-pending)',
  Cancelled: 'var(--status-cancelled)'
};

const statusIcons: Record<TestStatus, string> = {
  Passed: '✓',
  Failed: '✕',
  Running: '◉',
  Pending: '○',
  Cancelled: '⊘'
};

export function ResultsPanel({ results, runningProgress }: ResultsPanelProps) {
  const [expandedId, setExpandedId] = useState<string | null>(null);
  
  if (results.length === 0 && runningProgress.length === 0) {
    return (
      <div className="results-panel empty">
        <div className="empty-state">
          <span className="empty-icon">📋</span>
          <h3>No Test Results Yet</h3>
          <p>Run a test scenario to see results here</p>
        </div>
      </div>
    );
  }

  return (
    <section className="results-panel">
      <div className="section-header">
        <h2>Test Results</h2>
        <span className="results-count">{results.length} completed</span>
      </div>
      
      <div className="results-list">
        {results.map((result, index) => (
          <div 
            key={result.scenarioId}
            className={`result-item animate-in ${expandedId === result.scenarioId ? 'expanded' : ''}`}
            style={{ animationDelay: `${index * 0.05}s` }}
          >
            <div 
              className="result-header"
              onClick={() => setExpandedId(
                expandedId === result.scenarioId ? null : result.scenarioId
              )}
            >
              <div className="result-status" style={{ '--status-color': statusColors[result.status] } as React.CSSProperties}>
                <span className="status-icon">{statusIcons[result.status]}</span>
              </div>
              
              <div className="result-info">
                <span className="result-name">{result.scenarioName}</span>
                <span className="result-id">{result.scenarioId}</span>
              </div>
              
              <div className="result-metrics">
                {result.throughputPerSecond > 0 && (
                  <div className="metric">
                    <span className="metric-value">{result.throughputPerSecond.toFixed(1)}</span>
                    <span className="metric-label">/sec</span>
                  </div>
                )}
                {result.averageLatencyMs > 0 && (
                  <div className="metric">
                    <span className="metric-value">{result.averageLatencyMs.toFixed(1)}</span>
                    <span className="metric-label">ms avg</span>
                  </div>
                )}
                <div className="metric">
                  <span className="metric-value">{result.tradesGenerated}</span>
                  <span className="metric-label">trades</span>
                </div>
              </div>
              
              <div className="result-time">
                {result.duration && (
                  <span className="duration">{formatDuration(result.duration)}</span>
                )}
                <span className="timestamp">{formatTime(result.startedAt)}</span>
              </div>
              
              <span className="expand-icon">{expandedId === result.scenarioId ? '▲' : '▼'}</span>
            </div>
            
            {expandedId === result.scenarioId && (
              <div className="result-details">
                <div className="details-grid">
                  <div className="detail-section">
                    <h4>Performance Metrics</h4>
                    <div className="detail-row">
                      <span>Throughput</span>
                      <span className="value">{result.throughputPerSecond.toFixed(2)} trades/sec</span>
                    </div>
                    <div className="detail-row">
                      <span>Avg Latency</span>
                      <span className="value">{result.averageLatencyMs.toFixed(2)} ms</span>
                    </div>
                    <div className="detail-row">
                      <span>P95 Latency</span>
                      <span className="value">{result.p95LatencyMs.toFixed(2)} ms</span>
                    </div>
                    <div className="detail-row">
                      <span>P99 Latency</span>
                      <span className="value">{result.p99LatencyMs.toFixed(2)} ms</span>
                    </div>
                  </div>
                  
                  <div className="detail-section">
                    <h4>Data Flow</h4>
                    <div className="detail-row">
                      <span>Generated</span>
                      <span className="value">{result.tradesGenerated.toLocaleString()}</span>
                    </div>
                    <div className="detail-row">
                      <span>P&L Processed</span>
                      <span className="value">{result.tradesProcessedByPnl.toLocaleString()}</span>
                    </div>
                    <div className="detail-row">
                      <span>SQL Inserted</span>
                      <span className="value">{result.tradesInsertedToSql.toLocaleString()}</span>
                    </div>
                    <div className="detail-row">
                      <span>Errors</span>
                      <span className={`value ${result.errorCount > 0 ? 'error' : 'success'}`}>
                        {result.errorCount}
                      </span>
                    </div>
                  </div>
                </div>
                
                {result.steps.length > 0 && (
                  <div className="steps-section">
                    <h4>Test Steps</h4>
                    <div className="steps-list">
                      {result.steps.map((step, i) => (
                        <div 
                          key={i} 
                          className="step-item"
                          style={{ '--status-color': statusColors[step.status] } as React.CSSProperties}
                        >
                          <span className="step-icon">{statusIcons[step.status]}</span>
                          <span className="step-name">{step.name}</span>
                          {step.message && (
                            <span className="step-message">{step.message}</span>
                          )}
                          {step.duration && (
                            <span className="step-duration">{step.duration}</span>
                          )}
                        </div>
                      ))}
                    </div>
                  </div>
                )}
                
                {result.errors.length > 0 && (
                  <div className="errors-section">
                    <h4>Errors</h4>
                    <ul className="errors-list">
                      {result.errors.map((error, i) => (
                        <li key={i} className="error-item">{error}</li>
                      ))}
                    </ul>
                  </div>
                )}
              </div>
            )}
          </div>
        ))}
      </div>
    </section>
  );
}

function formatDuration(duration: string): string {
  // Parse .NET TimeSpan format (HH:MM:SS.ffffff)
  const parts = duration.split(':');
  if (parts.length === 3) {
    const [hours, minutes, secondsFull] = parts;
    const seconds = parseFloat(secondsFull);
    if (parseInt(hours) > 0) return `${hours}h ${minutes}m`;
    if (parseInt(minutes) > 0) return `${minutes}m ${Math.floor(seconds)}s`;
    return `${seconds.toFixed(1)}s`;
  }
  return duration;
}

function formatTime(timestamp: string): string {
  return new Date(timestamp).toLocaleTimeString();
}
