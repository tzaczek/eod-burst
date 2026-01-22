import './PathComparisonChart.css';

interface PathInfo {
  name: string;
  latency: string;
  accuracy: string;
  useCase: string;
  storage: string;
  color: string;
}

interface PathComparisonChartProps {
  hotPath: PathInfo;
  coldPath: PathInfo;
}

export function PathComparisonChart({ hotPath, coldPath }: PathComparisonChartProps) {
  return (
    <div className="path-comparison">
      <div className="comparison-header">
        <h3>
          <span className="header-icon">🔀</span>
          The Divergence of Speed and Truth
        </h3>
        <p className="comparison-subtitle">
          The business needs two contradictory capabilities simultaneously - solved via CQRS pattern
        </p>
      </div>

      <div className="comparison-grid">
        {/* Hot Path Card */}
        <div className="path-card hot">
          <div className="path-header">
            <span className="path-icon">🔥</span>
            <span className="path-title">{hotPath.name}</span>
          </div>

          <div className="path-metrics">
            <div className="metric-row">
              <span className="metric-label">
                <span className="label-icon">⏱️</span>
                Latency
              </span>
              <span className="metric-value hot">{hotPath.latency}</span>
            </div>
            <div className="metric-row">
              <span className="metric-label">
                <span className="label-icon">🎯</span>
                Accuracy
              </span>
              <span className="metric-value">{hotPath.accuracy}</span>
            </div>
            <div className="metric-row">
              <span className="metric-label">
                <span className="label-icon">👤</span>
                Use Case
              </span>
              <span className="metric-value">{hotPath.useCase}</span>
            </div>
            <div className="metric-row">
              <span className="metric-label">
                <span className="label-icon">💾</span>
                Storage
              </span>
              <span className="metric-value">{hotPath.storage}</span>
            </div>
          </div>

          <div className="path-visual">
            <div className="visual-bar hot" style={{ width: '95%' }}>
              <span className="bar-label">Speed Priority</span>
            </div>
          </div>
        </div>

        {/* VS Divider */}
        <div className="vs-divider">
          <div className="vs-line"></div>
          <span className="vs-text">VS</span>
          <div className="vs-line"></div>
        </div>

        {/* Cold Path Card */}
        <div className="path-card cold">
          <div className="path-header">
            <span className="path-icon">❄️</span>
            <span className="path-title">{coldPath.name}</span>
          </div>

          <div className="path-metrics">
            <div className="metric-row">
              <span className="metric-label">
                <span className="label-icon">⏱️</span>
                Latency
              </span>
              <span className="metric-value">{coldPath.latency}</span>
            </div>
            <div className="metric-row">
              <span className="metric-label">
                <span className="label-icon">🎯</span>
                Accuracy
              </span>
              <span className="metric-value cold">{coldPath.accuracy}</span>
            </div>
            <div className="metric-row">
              <span className="metric-label">
                <span className="label-icon">👤</span>
                Use Case
              </span>
              <span className="metric-value">{coldPath.useCase}</span>
            </div>
            <div className="metric-row">
              <span className="metric-label">
                <span className="label-icon">💾</span>
                Storage
              </span>
              <span className="metric-value">{coldPath.storage}</span>
            </div>
          </div>

          <div className="path-visual">
            <div className="visual-bar cold" style={{ width: '100%' }}>
              <span className="bar-label">Truth Priority</span>
            </div>
          </div>
        </div>
      </div>

      <div className="solution-banner">
        <span className="solution-icon">💡</span>
        <div className="solution-content">
          <span className="solution-title">Solution: CQRS Pattern</span>
          <span className="solution-desc">Split the pipeline at the source - separate read-optimized from write-optimized processing</span>
        </div>
      </div>
    </div>
  );
}
