import { useState } from 'react';
import type { TechStackItem } from '../constants/architecture';
import './TechStackChart.css';

interface TechStackChartProps {
  items: TechStackItem[];
}

export function TechStackChart({ items }: TechStackChartProps) {
  const [selectedTech, setSelectedTech] = useState<TechStackItem | null>(null);

  return (
    <div className="tech-stack-chart">
      <div className="tech-grid">
        {items.map((item, index) => (
          <div 
            key={item.layer} 
            className="tech-card"
            onClick={() => setSelectedTech(item)}
            style={{ 
              animationDelay: `${index * 0.05}s`,
              '--tech-color': item.color 
            } as React.CSSProperties}
          >
            <div className="tech-icon-wrapper">
              <span className="tech-icon">{item.icon}</span>
            </div>
            <div className="tech-content">
              <span className="tech-layer">{item.layer}</span>
              <span className="tech-name">{item.technology}</span>
              <span className="tech-desc">{item.description}</span>
            </div>
            <div className="tech-indicator" style={{ backgroundColor: item.color }}></div>
            <div className="tech-click-hint">Click for details</div>
          </div>
        ))}
      </div>

      {/* Technology Detail Modal */}
      {selectedTech && (
        <div className="tech-modal-overlay" onClick={() => setSelectedTech(null)}>
          <div 
            className="tech-modal" 
            onClick={(e) => e.stopPropagation()}
            style={{ '--tech-color': selectedTech.color } as React.CSSProperties}
          >
            <button className="tech-modal-close" onClick={() => setSelectedTech(null)}>
              ✕
            </button>
            <div className="tech-modal-header">
              <span className="tech-modal-icon">{selectedTech.icon}</span>
              <div className="tech-modal-title">
                <span className="tech-modal-layer">{selectedTech.layer}</span>
                <h3>{selectedTech.technology}</h3>
              </div>
            </div>
            <div className="tech-modal-body">
              <p className="tech-modal-details">{selectedTech.details}</p>
              
              {/* Code Example Section */}
              <div className="code-example-section">
                <div className="code-example-header">
                  <span className="code-example-title">{selectedTech.codeExample.title}</span>
                  {selectedTech.codeExample.file && (
                    <span className="code-example-file">
                      <span className="file-icon">📄</span>
                      {selectedTech.codeExample.file}
                    </span>
                  )}
                </div>
                <div className="code-example-wrapper">
                  <div className="code-language-badge">{selectedTech.codeExample.language}</div>
                  <pre className="code-example-pre">
                    <code className={`language-${selectedTech.codeExample.language}`}>
                      {selectedTech.codeExample.code}
                    </code>
                  </pre>
                </div>
              </div>
            </div>
            <div className="tech-modal-footer">
              <span 
                className="tech-modal-badge"
                style={{ backgroundColor: selectedTech.color }}
              >
                {selectedTech.layer} Layer
              </span>
            </div>
          </div>
        </div>
      )}

      {/* Visual Stack Representation */}
      <div className="stack-visual">
        <h4 className="stack-title">Technology Layers</h4>
        <div className="stack-layers">
          {[
            { name: 'Observability', color: '#E6522C', items: ['Prometheus', 'Grafana', 'Jaeger'] },
            { name: 'Application', color: '#00d9ff', items: ['.NET 8', 'C# 12', 'ASP.NET Core'] },
            { name: 'Messaging', color: '#FF6B35', items: ['Kafka', 'Schema Registry', 'Protobuf'] },
            { name: 'Data', color: '#00ff9f', items: ['Redis', 'SQL Server', 'MinIO'] },
            { name: 'Infrastructure', color: '#2496ED', items: ['Docker', 'Docker Compose'] },
          ].map((layer, index) => (
            <div 
              key={layer.name} 
              className="stack-layer"
              style={{ 
                '--layer-color': layer.color,
                animationDelay: `${(items.length + index) * 0.05}s`
              } as React.CSSProperties}
            >
              <div className="layer-header">
                <span className="layer-name">{layer.name}</span>
              </div>
              <div className="layer-items">
                {layer.items.map((item) => (
                  <span key={item} className="layer-item">{item}</span>
                ))}
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
