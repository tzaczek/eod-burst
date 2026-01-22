import { useState } from 'react';
import './CircuitBreakerDiagram.css';

interface CircuitBreakerState {
  state: string;
  description: string;
  color: string;
}

interface CircuitBreakerDiagramProps {
  states: CircuitBreakerState[];
}

export function CircuitBreakerDiagram({ states }: CircuitBreakerDiagramProps) {
  const [activeState, setActiveState] = useState<string>('CLOSED');
  const [isAnimating, setIsAnimating] = useState(false);

  // Demo animation cycle
  const runDemo = () => {
    setIsAnimating(true);
    const sequence = ['CLOSED', 'CLOSED', 'OPEN', 'OPEN', 'HALF-OPEN', 'CLOSED'];
    let index = 0;

    const interval = setInterval(() => {
      setActiveState(sequence[index]);
      index++;
      if (index >= sequence.length) {
        clearInterval(interval);
        setIsAnimating(false);
      }
    }, 1000);
  };

  return (
    <div className="circuit-breaker-diagram">
      <div className="cb-header">
        <h3>
          <span className="header-icon">🔌</span>
          Circuit Breaker State Machine
        </h3>
        <button 
          className="demo-btn" 
          onClick={runDemo} 
          disabled={isAnimating}
        >
          {isAnimating ? 'Running Demo...' : 'Run State Demo'}
        </button>
      </div>

      <div className="state-machine">
        {/* CLOSED State */}
        <div className={`state-node ${activeState === 'CLOSED' ? 'active' : ''}`}>
          <div className="state-indicator closed"></div>
          <span className="state-name">CLOSED</span>
          <span className="state-desc">Normal Operation</span>
          <span className="state-detail">Requests pass through</span>
        </div>

        {/* Transition: CLOSED → OPEN */}
        <div className="transition right">
          <div className="transition-line"></div>
          <div className="transition-arrow">▶</div>
          <span className="transition-label">Failure threshold exceeded</span>
        </div>

        {/* OPEN State */}
        <div className={`state-node ${activeState === 'OPEN' ? 'active' : ''}`}>
          <div className="state-indicator open"></div>
          <span className="state-name">OPEN</span>
          <span className="state-desc">Fail Fast</span>
          <span className="state-detail">Requests blocked</span>
        </div>

        {/* Transition: OPEN → HALF-OPEN */}
        <div className="transition down">
          <div className="transition-line vertical"></div>
          <div className="transition-arrow">▼</div>
          <span className="transition-label">OpenDuration expires</span>
        </div>

        {/* HALF-OPEN State */}
        <div className={`state-node ${activeState === 'HALF-OPEN' ? 'active' : ''}`}>
          <div className="state-indicator half-open"></div>
          <span className="state-name">HALF-OPEN</span>
          <span className="state-desc">Test Mode</span>
          <span className="state-detail">Limited requests allowed</span>
        </div>

        {/* Transition: HALF-OPEN → CLOSED (success) */}
        <div className="transition left success">
          <div className="transition-line"></div>
          <div className="transition-arrow">◀</div>
          <span className="transition-label">Success threshold reached</span>
        </div>

        {/* Transition: HALF-OPEN → OPEN (failure) */}
        <div className="transition up failure">
          <div className="transition-line vertical"></div>
          <div className="transition-arrow">▲</div>
          <span className="transition-label">Failure occurs</span>
        </div>
      </div>

      {/* State Legend */}
      <div className="state-legend">
        {states.map((state) => (
          <div 
            key={state.state} 
            className={`legend-item ${activeState === state.state ? 'active' : ''}`}
            onClick={() => setActiveState(state.state)}
          >
            <span 
              className="legend-dot" 
              style={{ backgroundColor: state.color, boxShadow: `0 0 10px ${state.color}` }}
            ></span>
            <div className="legend-content">
              <span className="legend-state">{state.state}</span>
              <span className="legend-desc">{state.description}</span>
            </div>
          </div>
        ))}
      </div>

      {/* Configuration Presets */}
      <div className="presets-container">
        <h4>Configuration Presets</h4>
        <div className="presets-grid">
          <div className="preset-card">
            <span className="preset-name">HighAvailability</span>
            <div className="preset-config">
              <span>3 failures</span>
              <span>15s open</span>
            </div>
          </div>
          <div className="preset-card">
            <span className="preset-name">ExternalService</span>
            <div className="preset-config">
              <span>5 failures</span>
              <span>60s open</span>
            </div>
          </div>
          <div className="preset-card">
            <span className="preset-name">Storage</span>
            <div className="preset-config">
              <span>10 failures</span>
              <span>30s open</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
