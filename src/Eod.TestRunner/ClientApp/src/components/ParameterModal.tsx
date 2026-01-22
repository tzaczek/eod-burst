import { useState } from 'react';
import { TestScenario, TestParameters } from '../types';
import './ParameterModal.css';

interface ParameterModalProps {
  scenario: TestScenario;
  onRun: (params: Partial<TestParameters>) => void;
  onClose: () => void;
}

export function ParameterModal({ scenario, onRun, onClose }: ParameterModalProps) {
  const [params, setParams] = useState<TestParameters>({ ...scenario.parameters });
  
  const handleChange = (field: keyof TestParameters, value: string | number | string[]) => {
    setParams(prev => ({ ...prev, [field]: value }));
  };
  
  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onRun(params);
  };
  
  const handleQuickRun = () => {
    onRun({});
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <div>
            <h2>{scenario.name}</h2>
            <p className="modal-description">{scenario.description}</p>
          </div>
          <button className="close-btn" onClick={onClose}>✕</button>
        </div>
        
        <form onSubmit={handleSubmit}>
          <div className="params-grid">
            {/* Trade Generation Parameters */}
            <div className="param-group">
              <h3>Trade Generation</h3>
              
              <div className="param-field">
                <label htmlFor="tradeCount">Trade Count</label>
                <input
                  id="tradeCount"
                  type="number"
                  min="1"
                  max="100000"
                  value={params.tradeCount}
                  onChange={e => handleChange('tradeCount', parseInt(e.target.value) || 0)}
                />
                <span className="param-hint">Number of trades to generate</span>
              </div>
              
              <div className="param-field">
                <label htmlFor="tradesPerSecond">Trades per Second</label>
                <input
                  id="tradesPerSecond"
                  type="number"
                  min="1"
                  max="10000"
                  value={params.tradesPerSecond}
                  onChange={e => handleChange('tradesPerSecond', parseInt(e.target.value) || 0)}
                />
                <span className="param-hint">Target throughput rate</span>
              </div>
              
              {scenario.type === 'BurstMode' && (
                <>
                  <div className="param-field">
                    <label htmlFor="burstMultiplier">Burst Multiplier</label>
                    <input
                      id="burstMultiplier"
                      type="number"
                      min="2"
                      max="100"
                      value={params.burstMultiplier}
                      onChange={e => handleChange('burstMultiplier', parseInt(e.target.value) || 0)}
                    />
                    <span className="param-hint">Traffic spike multiplier (e.g., 10x)</span>
                  </div>
                  
                  <div className="param-field">
                    <label htmlFor="burstDuration">Burst Duration (sec)</label>
                    <input
                      id="burstDuration"
                      type="number"
                      min="5"
                      max="300"
                      value={params.burstDurationSeconds}
                      onChange={e => handleChange('burstDurationSeconds', parseInt(e.target.value) || 0)}
                    />
                    <span className="param-hint">How long the burst lasts</span>
                  </div>
                </>
              )}
            </div>
            
            {/* Symbols & Traders */}
            <div className="param-group">
              <h3>Test Data</h3>
              
              <div className="param-field">
                <label htmlFor="symbols">Symbols</label>
                <input
                  id="symbols"
                  type="text"
                  value={params.symbols.join(', ')}
                  onChange={e => handleChange('symbols', e.target.value.split(',').map(s => s.trim()).filter(Boolean))}
                />
                <span className="param-hint">Comma-separated: AAPL, MSFT, GOOGL</span>
              </div>
              
              <div className="param-field">
                <label htmlFor="traderIds">Trader IDs</label>
                <input
                  id="traderIds"
                  type="text"
                  value={params.traderIds.join(', ')}
                  onChange={e => handleChange('traderIds', e.target.value.split(',').map(s => s.trim()).filter(Boolean))}
                />
                <span className="param-hint">Comma-separated: T001, T002</span>
              </div>
            </div>
            
            {/* Timing & Validation */}
            <div className="param-group">
              <h3>Timing & Validation</h3>
              
              <div className="param-field">
                <label htmlFor="timeout">Timeout (sec)</label>
                <input
                  id="timeout"
                  type="number"
                  min="10"
                  max="600"
                  value={params.timeoutSeconds}
                  onChange={e => handleChange('timeoutSeconds', parseInt(e.target.value) || 0)}
                />
                <span className="param-hint">Maximum test duration</span>
              </div>
              
              <div className="param-field">
                <label htmlFor="warmup">Warmup (sec)</label>
                <input
                  id="warmup"
                  type="number"
                  min="0"
                  max="60"
                  value={params.warmupSeconds}
                  onChange={e => handleChange('warmupSeconds', parseInt(e.target.value) || 0)}
                />
                <span className="param-hint">Warmup period before measurement</span>
              </div>
              
              {scenario.type === 'Latency' && (
                <div className="param-field">
                  <label htmlFor="expectedLatency">Expected Latency (ms)</label>
                  <input
                    id="expectedLatency"
                    type="number"
                    min="1"
                    max="10000"
                    value={params.expectedLatencyMs}
                    onChange={e => handleChange('expectedLatencyMs', parseInt(e.target.value) || 0)}
                  />
                  <span className="param-hint">P95 latency SLA target</span>
                </div>
              )}
            </div>
          </div>
          
          <div className="modal-actions">
            <button type="button" className="btn btn-secondary" onClick={onClose}>
              Cancel
            </button>
            <button type="button" className="btn btn-default" onClick={handleQuickRun}>
              Quick Run (Defaults)
            </button>
            <button type="submit" className="btn btn-primary">
              <span className="btn-icon">▶</span>
              Run with Parameters
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
