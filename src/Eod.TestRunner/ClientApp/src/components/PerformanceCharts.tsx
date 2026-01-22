import { useState, useEffect } from 'react';
import { 
  AreaChart, 
  Area, 
  XAxis, 
  YAxis, 
  CartesianGrid, 
  Tooltip, 
  ResponsiveContainer,
  BarChart,
  Bar,
  PieChart,
  Pie,
  Cell
} from 'recharts';
import './PerformanceCharts.css';

// Simulated throughput data showing EOD burst pattern
const generateThroughputData = () => {
  const data = [];
  for (let i = 0; i < 24; i++) {
    const hour = i;
    let value = 100 + Math.random() * 50; // Base trading activity
    
    // Morning opening bell spike
    if (hour >= 9 && hour <= 10) {
      value = 2000 + Math.random() * 500;
    }
    
    // EOD burst at 4 PM (hour 16)
    if (hour === 15) {
      value = 5000 + Math.random() * 1000;
    }
    if (hour === 16) {
      value = 10000 + Math.random() * 2000; // Peak!
    }
    
    // After hours
    if (hour > 16 || hour < 9) {
      value = 50 + Math.random() * 30;
    }
    
    data.push({
      hour: `${hour}:00`,
      trades: Math.round(value),
      label: hour === 16 ? 'EOD BURST' : ''
    });
  }
  return data;
};

// Latency comparison data
const latencyData = [
  { name: 'Flash P&L', latency: 85, target: 100, color: '#ff6b35' },
  { name: 'Ingestion', latency: 12, target: 50, color: '#00d9ff' },
  { name: 'Kafka Pub', latency: 8, target: 20, color: '#00ff9f' },
  { name: 'Redis Op', latency: 0.5, target: 5, color: '#ffd700' },
  { name: 'SQL Insert', latency: 450, target: 1000, color: '#ff006e' },
];

// Data flow distribution
const flowDistributionData = [
  { name: 'Hot Path (Flash P&L)', value: 100, color: '#ff6b35' },
  { name: 'Cold Path (Regulatory)', value: 100, color: '#00d9ff' },
  { name: 'DLQ (Errors)', value: 0.1, color: '#ff006e' },
];

export function PerformanceCharts() {
  const [throughputData, setThroughputData] = useState(generateThroughputData);

  useEffect(() => {
    // Regenerate data periodically to show live feel
    const interval = setInterval(() => {
      setThroughputData(generateThroughputData());
    }, 10000);
    
    return () => clearInterval(interval);
  }, []);

  return (
    <div className="performance-charts">
      {/* Throughput Chart */}
      <div className="chart-card">
        <div className="chart-header">
          <h4>
            <span className="chart-icon">📈</span>
            Daily Trading Volume Pattern
          </h4>
          <span className="chart-badge">trades/second</span>
        </div>
        <div className="chart-container">
          <ResponsiveContainer width="100%" height={250}>
            <AreaChart data={throughputData} margin={{ top: 10, right: 30, left: 0, bottom: 0 }}>
              <defs>
                <linearGradient id="throughputGradient" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#00d9ff" stopOpacity={0.4}/>
                  <stop offset="95%" stopColor="#00d9ff" stopOpacity={0}/>
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="#30363d" />
              <XAxis 
                dataKey="hour" 
                stroke="#6e7681" 
                fontSize={11}
                tickLine={false}
              />
              <YAxis 
                stroke="#6e7681" 
                fontSize={11}
                tickLine={false}
                tickFormatter={(value) => value >= 1000 ? `${(value/1000).toFixed(0)}K` : value}
              />
              <Tooltip 
                contentStyle={{ 
                  backgroundColor: '#1a222d', 
                  border: '1px solid #30363d',
                  borderRadius: '8px',
                  color: '#e6edf3'
                }}
                formatter={(value: number) => [`${value.toLocaleString()} trades/sec`, 'Throughput']}
              />
              <Area 
                type="monotone" 
                dataKey="trades" 
                stroke="#00d9ff" 
                strokeWidth={2}
                fillOpacity={1} 
                fill="url(#throughputGradient)"
                animationDuration={2000}
              />
            </AreaChart>
          </ResponsiveContainer>
        </div>
        <div className="chart-highlight">
          <span className="highlight-label">Peak at 4:00 PM:</span>
          <span className="highlight-value">10,000+ trades/sec</span>
        </div>
      </div>

      {/* Latency Chart */}
      <div className="chart-card">
        <div className="chart-header">
          <h4>
            <span className="chart-icon">⏱️</span>
            Component Latency vs SLA
          </h4>
          <span className="chart-badge">milliseconds</span>
        </div>
        <div className="chart-container">
          <ResponsiveContainer width="100%" height={200}>
            <BarChart data={latencyData} layout="vertical" margin={{ top: 5, right: 30, left: 80, bottom: 5 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#30363d" horizontal={false} />
              <XAxis 
                type="number" 
                stroke="#6e7681" 
                fontSize={11}
                tickLine={false}
              />
              <YAxis 
                type="category" 
                dataKey="name" 
                stroke="#6e7681" 
                fontSize={11}
                tickLine={false}
                width={75}
              />
              <Tooltip 
                contentStyle={{ 
                  backgroundColor: '#1a222d', 
                  border: '1px solid #30363d',
                  borderRadius: '8px',
                  color: '#e6edf3'
                }}
                formatter={(value: number, name: string) => [
                  `${value}ms ${name === 'latency' ? '(actual)' : '(target)'}`,
                  name === 'latency' ? 'Actual' : 'SLA Target'
                ]}
              />
              <Bar 
                dataKey="latency" 
                fill="#00d9ff"
                radius={[0, 4, 4, 0]}
                animationDuration={1500}
              >
                {latencyData.map((entry, index) => (
                  <Cell key={`cell-${index}`} fill={entry.color} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        </div>
        <div className="latency-legend">
          {latencyData.map((item) => (
            <div key={item.name} className="latency-item">
              <span className="latency-dot" style={{ backgroundColor: item.color }}></span>
              <span className="latency-name">{item.name}</span>
              <span className="latency-value">{item.latency}ms</span>
              <span className="latency-target">/ {item.target}ms SLA</span>
            </div>
          ))}
        </div>
      </div>

      {/* Data Flow Distribution */}
      <div className="chart-card small">
        <div className="chart-header">
          <h4>
            <span className="chart-icon">🔀</span>
            Message Distribution
          </h4>
        </div>
        <div className="chart-container pie-container">
          <ResponsiveContainer width="100%" height={180}>
            <PieChart>
              <Pie
                data={flowDistributionData}
                cx="50%"
                cy="50%"
                innerRadius={50}
                outerRadius={70}
                paddingAngle={2}
                dataKey="value"
                animationDuration={1500}
              >
                {flowDistributionData.map((entry, index) => (
                  <Cell key={`cell-${index}`} fill={entry.color} />
                ))}
              </Pie>
              <Tooltip 
                contentStyle={{ 
                  backgroundColor: '#1a222d', 
                  border: '1px solid #30363d',
                  borderRadius: '8px',
                  color: '#e6edf3'
                }}
              />
            </PieChart>
          </ResponsiveContainer>
          <div className="pie-legend">
            {flowDistributionData.map((item) => (
              <div key={item.name} className="pie-legend-item">
                <span className="pie-dot" style={{ backgroundColor: item.color }}></span>
                <span>{item.name}</span>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Key Metrics Summary */}
      <div className="metrics-summary">
        <div className="summary-item">
          <span className="summary-icon">🚀</span>
          <div className="summary-content">
            <span className="summary-value">10K+</span>
            <span className="summary-label">trades/sec peak</span>
          </div>
        </div>
        <div className="summary-item">
          <span className="summary-icon">⚡</span>
          <div className="summary-content">
            <span className="summary-value">&lt;100ms</span>
            <span className="summary-label">P&L latency</span>
          </div>
        </div>
        <div className="summary-item">
          <span className="summary-icon">📈</span>
          <div className="summary-content">
            <span className="summary-value">10x</span>
            <span className="summary-label">burst capacity</span>
          </div>
        </div>
        <div className="summary-item">
          <span className="summary-icon">🎯</span>
          <div className="summary-content">
            <span className="summary-value">100%</span>
            <span className="summary-label">regulatory accuracy</span>
          </div>
        </div>
      </div>
    </div>
  );
}
