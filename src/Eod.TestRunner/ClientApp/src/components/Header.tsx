import { useState } from 'react';
import './Header.css';

interface HeaderProps {
  isConnected: boolean;
}

const grafanaDashboards = [
  { name: 'Overview', path: '/d/eod-overview', icon: '📈' },
  { name: 'Message Flow', path: '/d/eod-flow', icon: '🔀' },
  { name: 'Kafka', path: '/d/eod-kafka', icon: '📨' },
  { name: 'Redis', path: '/d/eod-redis', icon: '💾' },
  { name: 'Service Detail', path: '/d/eod-service-detail', icon: '🔧' },
  { name: 'Tracing', path: '/d/eod-tracing', icon: '🔗' },
];

export function Header({ isConnected }: HeaderProps) {
  const [showDropdown, setShowDropdown] = useState(false);

  return (
    <header className="header">
      <div className="header-content">
        <div className="logo-section">
          <div className="logo">
            <span className="logo-icon">⚡</span>
            <span className="logo-text">EOD BURST</span>
          </div>
          <span className="logo-subtitle">End-to-End Test Dashboard</span>
        </div>
        
        <div className="header-status">
          <div className={`connection-status ${isConnected ? 'connected' : 'disconnected'}`}>
            <span className="status-dot"></span>
            <span className="status-text">
              {isConnected ? 'Live Updates Active' : 'Reconnecting...'}
            </span>
          </div>
          
          <div 
            className="dropdown-container"
            onMouseEnter={() => setShowDropdown(true)}
            onMouseLeave={() => setShowDropdown(false)}
          >
            <button className="grafana-link">
              <span className="link-icon">📊</span>
              Grafana
              <span className="dropdown-arrow">▼</span>
            </button>
            {showDropdown && (
              <div className="dropdown-menu">
                <div className="dropdown-header">EOD Burst Dashboards</div>
                {grafanaDashboards.map((dashboard) => (
                  <a
                    key={dashboard.path}
                    href={`http://localhost:3000${dashboard.path}`}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="dropdown-item"
                  >
                    <span className="dropdown-icon">{dashboard.icon}</span>
                    {dashboard.name}
                  </a>
                ))}
              </div>
            )}
          </div>
          
          <a 
            href="http://localhost:16686" 
            target="_blank" 
            rel="noopener noreferrer"
            className="jaeger-link"
          >
            <span className="link-icon">🔍</span>
            Jaeger
          </a>
        </div>
      </div>
    </header>
  );
}
