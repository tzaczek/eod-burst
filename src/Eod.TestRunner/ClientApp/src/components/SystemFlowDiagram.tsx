import { useState } from 'react';
import './SystemFlowDiagram.css';

interface ServiceNode {
  id: string;
  name: string;
  icon: string;
  description: string;
  type: 'source' | 'service' | 'queue' | 'storage';
  metrics?: string[];
}

const NODES: ServiceNode[] = [
  { id: 'fix', name: 'FIX Protocol', icon: '📡', description: 'External market data feeds', type: 'source' },
  { id: 'ingestion', name: 'Ingestion', icon: '🔄', description: 'Validates, archives, publishes', type: 'service', metrics: ['10K trades/sec', 'Checksum validation'] },
  { id: 'minio', name: 'MinIO (S3)', icon: '📦', description: 'Raw FIX archive', type: 'storage' },
  { id: 'kafka', name: 'Kafka', icon: '📨', description: 'trades.raw topic', type: 'queue', metrics: ['12 partitions', '7 days retention'] },
  { id: 'flashpnl', name: 'Flash P&L', icon: '⚡', description: 'Real-time calculations', type: 'service', metrics: ['<100ms latency', 'In-memory positions'] },
  { id: 'regulatory', name: 'Regulatory', icon: '📋', description: 'Compliance persistence', type: 'service', metrics: ['100% accuracy', 'Batch processing'] },
  { id: 'redis', name: 'Redis', icon: '💾', description: 'Position cache & pub/sub', type: 'storage', metrics: ['Sub-ms latency', 'HSET operations'] },
  { id: 'sqlserver', name: 'SQL Server', icon: '🗄️', description: 'Audit trail storage', type: 'storage', metrics: ['SqlBulkCopy', 'ACID compliant'] },
  { id: 'dlq', name: 'DLQ', icon: '📬', description: 'trades.dlq', type: 'queue' },
];

export function SystemFlowDiagram() {
  const [hoveredNode, setHoveredNode] = useState<string | null>(null);
  const [animateFlow, setAnimateFlow] = useState(false);

  const startAnimation = () => {
    setAnimateFlow(true);
    setTimeout(() => setAnimateFlow(false), 3000);
  };

  return (
    <div className="system-flow-diagram">
      <div className="diagram-header">
        <h3>
          <span className="header-icon">🏗️</span>
          High-Level Architecture
        </h3>
        <button className="animate-btn" onClick={startAnimation} disabled={animateFlow}>
          <span className="btn-icon">▶</span>
          {animateFlow ? 'Animating...' : 'Animate Flow'}
        </button>
      </div>

      <div className="diagram-canvas">
        {/* Top Row: Source -> Ingestion -> Archive */}
        <div className="flow-row top-row">
          <Node 
            node={NODES[0]} 
            isHovered={hoveredNode === 'fix'} 
            onHover={setHoveredNode}
            animating={animateFlow}
            delay={0}
          />
          <Arrow direction="right" animating={animateFlow} delay={200} label="FIX msgs" />
          <Node 
            node={NODES[1]} 
            isHovered={hoveredNode === 'ingestion'} 
            onHover={setHoveredNode}
            animating={animateFlow}
            delay={400}
          />
          <Arrow direction="right" animating={animateFlow} delay={600} label="async" dashed />
          <Node 
            node={NODES[2]} 
            isHovered={hoveredNode === 'minio'} 
            onHover={setHoveredNode}
            animating={animateFlow}
            delay={800}
          />
        </div>

        {/* Connector from Ingestion down to Kafka */}
        <div className="vertical-connector left-connector">
          <Arrow direction="down" animating={animateFlow} delay={1000} label="Protobuf" />
        </div>

        {/* Middle Row: Kafka */}
        <div className="flow-row middle-row">
          <Node 
            node={NODES[3]} 
            isHovered={hoveredNode === 'kafka'} 
            onHover={setHoveredNode}
            animating={animateFlow}
            delay={1200}
            large
          />
        </div>

        {/* Split paths */}
        <div className="split-container">
          {/* Hot Path */}
          <div className="path-branch hot-branch">
            <div className="path-label hot">
              <span className="path-icon">🔥</span>
              HOT PATH
              <span className="path-latency">~100ms</span>
            </div>
            <Arrow direction="down" animating={animateFlow} delay={1400} />
            <Node 
              node={NODES[4]} 
              isHovered={hoveredNode === 'flashpnl'} 
              onHover={setHoveredNode}
              animating={animateFlow}
              delay={1600}
            />
            <Arrow direction="down" animating={animateFlow} delay={1800} />
            <Node 
              node={NODES[6]} 
              isHovered={hoveredNode === 'redis'} 
              onHover={setHoveredNode}
              animating={animateFlow}
              delay={2000}
            />
          </div>

          {/* Cold Path */}
          <div className="path-branch cold-branch">
            <div className="path-label cold">
              <span className="path-icon">❄️</span>
              COLD PATH
              <span className="path-latency">Hours OK</span>
            </div>
            <Arrow direction="down" animating={animateFlow} delay={1400} />
            <Node 
              node={NODES[5]} 
              isHovered={hoveredNode === 'regulatory'} 
              onHover={setHoveredNode}
              animating={animateFlow}
              delay={1600}
            />
            <Arrow direction="down" animating={animateFlow} delay={1800} />
            <Node 
              node={NODES[7]} 
              isHovered={hoveredNode === 'sqlserver'} 
              onHover={setHoveredNode}
              animating={animateFlow}
              delay={2000}
            />
          </div>

          {/* DLQ */}
          <div className="dlq-branch">
            <div className="dlq-connector">
              <Arrow direction="right" animating={animateFlow} delay={2200} dashed />
            </div>
            <Node 
              node={NODES[8]} 
              isHovered={hoveredNode === 'dlq'} 
              onHover={setHoveredNode}
              animating={animateFlow}
              delay={2400}
              small
            />
          </div>
        </div>
      </div>

      <div className="diagram-legend">
        <div className="legend-item">
          <span className="legend-color service"></span>
          <span>Application Service</span>
        </div>
        <div className="legend-item">
          <span className="legend-color queue"></span>
          <span>Message Queue</span>
        </div>
        <div className="legend-item">
          <span className="legend-color storage"></span>
          <span>Data Storage</span>
        </div>
        <div className="legend-item">
          <span className="legend-line dashed"></span>
          <span>Async/Optional</span>
        </div>
      </div>
    </div>
  );
}

interface NodeProps {
  node: ServiceNode;
  isHovered: boolean;
  onHover: (id: string | null) => void;
  animating: boolean;
  delay: number;
  large?: boolean;
  small?: boolean;
}

function Node({ node, isHovered, onHover, animating, delay, large, small }: NodeProps) {
  const className = [
    'flow-node',
    `node-${node.type}`,
    isHovered ? 'hovered' : '',
    animating ? 'pulse' : '',
    large ? 'large' : '',
    small ? 'small' : '',
  ].filter(Boolean).join(' ');

  return (
    <div 
      className={className}
      onMouseEnter={() => onHover(node.id)}
      onMouseLeave={() => onHover(null)}
      style={{ animationDelay: `${delay}ms` }}
    >
      <span className="node-icon">{node.icon}</span>
      <span className="node-name">{node.name}</span>
      
      {isHovered && (
        <div className="node-tooltip">
          <div className="tooltip-header">{node.name}</div>
          <p className="tooltip-desc">{node.description}</p>
          {node.metrics && (
            <div className="tooltip-metrics">
              {node.metrics.map((m, i) => (
                <span key={i} className="tooltip-metric">{m}</span>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

interface ArrowProps {
  direction: 'right' | 'down';
  animating: boolean;
  delay: number;
  label?: string;
  dashed?: boolean;
}

function Arrow({ direction, animating, delay, label, dashed }: ArrowProps) {
  const className = [
    'flow-arrow',
    `arrow-${direction}`,
    animating ? 'flowing' : '',
    dashed ? 'dashed' : '',
  ].filter(Boolean).join(' ');

  return (
    <div className={className} style={{ animationDelay: `${delay}ms` }}>
      <div className="arrow-line"></div>
      <div className="arrow-head">{direction === 'right' ? '▶' : '▼'}</div>
      {label && <span className="arrow-label">{label}</span>}
    </div>
  );
}
