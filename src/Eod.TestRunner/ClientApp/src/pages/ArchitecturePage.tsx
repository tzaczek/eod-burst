import { useState } from 'react';
import { 
  CAPABILITIES, 
  TECH_STACK, 
  DESIGN_PATTERNS, 
  SERVICES, 
  PATH_COMPARISON,
  DATA_FLOW_STEPS,
  TEST_SCENARIOS,
  GLOSSARY,
  CIRCUIT_BREAKER_STATES
} from '../constants/architecture';
import { SystemFlowDiagram } from '../components/SystemFlowDiagram';
import { TechStackChart } from '../components/TechStackChart';
import { PathComparisonChart } from '../components/PathComparisonChart';
import { CircuitBreakerDiagram } from '../components/CircuitBreakerDiagram';
import { PerformanceCharts } from '../components/PerformanceCharts';
import './ArchitecturePage.css';

type SectionId = 'overview' | 'architecture' | 'techstack' | 'patterns' | 'dataflow' | 'resilience' | 'testing' | 'glossary';

const SECTIONS: { id: SectionId; name: string; icon: string }[] = [
  { id: 'overview', name: 'Summary', icon: '📋' },
  { id: 'architecture', name: 'System Architecture', icon: '🏗️' },
  { id: 'techstack', name: 'Technology Stack', icon: '⚙️' },
  { id: 'patterns', name: 'Design Patterns', icon: '🧩' },
  { id: 'dataflow', name: 'Data Flow', icon: '🔀' },
  { id: 'resilience', name: 'Resilience Patterns', icon: '🛡️' },
  { id: 'testing', name: 'Testing Infrastructure', icon: '🧪' },
  { id: 'glossary', name: 'Glossary', icon: '📚' },
];

export function ArchitecturePage() {
  const [activeSection, setActiveSection] = useState<SectionId>('overview');

  const scrollToSection = (id: SectionId) => {
    setActiveSection(id);
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' });
  };

  return (
    <div className="architecture-page">
      {/* Sidebar Navigation */}
      <aside className="arch-sidebar">
        <div className="sidebar-header">
          <span className="sidebar-icon">📑</span>
          <span className="sidebar-title">Documentation</span>
        </div>
        <nav className="sidebar-nav">
          {SECTIONS.map((section) => (
            <button
              key={section.id}
              className={`sidebar-link ${activeSection === section.id ? 'active' : ''}`}
              onClick={() => scrollToSection(section.id)}
            >
              <span className="link-icon">{section.icon}</span>
              {section.name}
            </button>
          ))}
        </nav>
      </aside>

      {/* Main Content */}
      <main className="arch-content">
        {/* Hero Section */}
        <header className="arch-hero">
          <div className="hero-badge">Production-Ready Architecture</div>
          <h1 className="hero-title">
            End of Day <span className="highlight">Burst System</span>
          </h1>
          <p className="hero-subtitle">
            High-performance, distributed trade processing platform designed to handle 
            the extreme load spike that occurs at market close (4:00 PM EST).
          </p>
          
          {/* GitHub CTA Banner */}
          <a 
            href="https://github.com/tzaczek/eod-burst" 
            target="_blank" 
            rel="noopener noreferrer"
            className="github-cta"
          >
            <div className="github-cta-content">
              <span className="github-cta-icon">⭐</span>
              <div className="github-cta-text">
                <span className="github-cta-title">Clone this repository!</span>
                <span className="github-cta-subtitle">Get a production-ready CQRS architecture running in minutes</span>
              </div>
              <span className="github-cta-arrow">→</span>
            </div>
          </a>
          
          <div className="hero-meta">
            <span className="meta-item">
              <span className="meta-icon">📅</span>
              Last Updated: 2026-01-22
            </span>
            <span className="meta-item">
              <span className="meta-icon">🏷️</span>
              Version 2.0.0
            </span>
          </div>
        </header>

        {/* Summary Section */}
        <section id="overview" className="arch-section">
          <div className="section-header">
            <span className="section-icon">📋</span>
            <h2>Summary</h2>
          </div>
          
          <div className="problem-statement">
            <h3>The Core Problem</h3>
            <p>
              At <strong>4:00:00 PM EST</strong> (market close), trading volume spikes 
              <span className="emphasis"> 100x</span> above daily average due to:
            </p>
            <ul className="problem-list">
              <li><span className="bullet">▸</span> NYSE D-Orders (discretionary orders released at close)</li>
              <li><span className="bullet">▸</span> Closing crosses (MOC/LOC orders)</li>
              <li><span className="bullet">▸</span> Algorithmic liquidations</li>
              <li><span className="bullet">▸</span> End-of-day position squaring</li>
            </ul>
          </div>

          <div className="capabilities-grid">
            {CAPABILITIES.map((cap) => (
              <div key={cap.name} className="capability-card">
                <span className="cap-icon">{cap.icon}</span>
                <div className="cap-content">
                  <span className="cap-value">{cap.value}</span>
                  <span className="cap-name">{cap.name}</span>
                </div>
              </div>
            ))}
          </div>

          <PathComparisonChart hotPath={PATH_COMPARISON.hotPath} coldPath={PATH_COMPARISON.coldPath} />

          <PerformanceCharts />
        </section>

        {/* System Architecture Section */}
        <section id="architecture" className="arch-section">
          <div className="section-header">
            <span className="section-icon">🏗️</span>
            <h2>System Architecture</h2>
          </div>

          <SystemFlowDiagram />

          <div className="services-container">
            <h3>Network Architecture</h3>
            <p className="services-intro">
              All services communicate within the <code>eod-network</code> Docker bridge network.
            </p>
            
            <div className="services-grid">
              {(['application', 'infrastructure', 'observability'] as const).map((category) => (
                <div key={category} className={`services-tier tier-${category}`}>
                  <h4 className="tier-title">
                    {category === 'application' && '🖥️ Application Tier'}
                    {category === 'infrastructure' && '⚙️ Infrastructure Tier'}
                    {category === 'observability' && '📊 Observability Tier'}
                  </h4>
                  <div className="tier-services">
                    {SERVICES.filter(s => s.category === category).map((service) => (
                      <div key={service.name} className="service-badge">
                        <span className="service-icon">{service.icon}</span>
                        <div className="service-info">
                          <span className="service-name">{service.name}</span>
                          <span className="service-port">:{service.port}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>
        </section>

        {/* Technology Stack Section */}
        <section id="techstack" className="arch-section">
          <div className="section-header">
            <span className="section-icon">⚙️</span>
            <h2>Technology Stack</h2>
          </div>

          <TechStackChart items={TECH_STACK} />
        </section>

        {/* Design Patterns Section */}
        <section id="patterns" className="arch-section">
          <div className="section-header">
            <span className="section-icon">🧩</span>
            <h2>Design Patterns</h2>
          </div>

          <div className="patterns-grid">
            {DESIGN_PATTERNS.map((pattern) => (
              <div key={pattern.name} className="pattern-card">
                <div className="pattern-header">
                  <span className="pattern-icon">{pattern.icon}</span>
                  <h4 className="pattern-name">{pattern.name}</h4>
                </div>
                <div className="pattern-impl">
                  <span className="impl-label">Implementation</span>
                  <code className="impl-code">{pattern.implementation}</code>
                </div>
                <p className="pattern-purpose">{pattern.purpose}</p>
              </div>
            ))}
          </div>
        </section>

        {/* Data Flow Section */}
        <section id="dataflow" className="arch-section">
          <div className="section-header">
            <span className="section-icon">🔀</span>
            <h2>Data Flow</h2>
          </div>

          <div className="dataflow-container">
            <div className="flow-steps">
              {DATA_FLOW_STEPS.map((step, index) => (
                <div key={step.step} className="flow-step" style={{ animationDelay: `${index * 0.1}s` }}>
                  <div className="step-number">{step.step}</div>
                  <div className="step-icon">{step.icon}</div>
                  <div className="step-content">
                    <span className="step-name">{step.name}</span>
                    <span className="step-service">{step.service}</span>
                  </div>
                  {index < DATA_FLOW_STEPS.length - 1 && (
                    <div className="step-connector">
                      <div className="connector-line"></div>
                      <div className="connector-arrow">▶</div>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        </section>

        {/* Resilience Patterns Section */}
        <section id="resilience" className="arch-section">
          <div className="section-header">
            <span className="section-icon">🛡️</span>
            <h2>Resilience Patterns</h2>
          </div>

          <CircuitBreakerDiagram states={CIRCUIT_BREAKER_STATES} />

          <div className="resilience-table-container">
            <h3>Behavior Under Failure</h3>
            <table className="resilience-table">
              <thead>
                <tr>
                  <th>Component</th>
                  <th>Circuit Breaker</th>
                  <th>Fallback</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td>S3 Archive</td>
                  <td>Open after 5 failures</td>
                  <td>Drop messages (acceptable loss)</td>
                </tr>
                <tr>
                  <td>Redis Publish</td>
                  <td>Open after 5 failures</td>
                  <td>Skip publish, process locally</td>
                </tr>
                <tr>
                  <td>Redis Query</td>
                  <td>Open after 10 failures</td>
                  <td>Use local price cache</td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        {/* Testing Infrastructure Section */}
        <section id="testing" className="arch-section">
          <div className="section-header">
            <span className="section-icon">🧪</span>
            <h2>Testing Infrastructure</h2>
          </div>

          <div className="testing-grid">
            {TEST_SCENARIOS.map((scenario) => (
              <div key={scenario.type} className="test-scenario-card">
                <div className="scenario-type">{scenario.type}</div>
                <p className="scenario-purpose">{scenario.purpose}</p>
                <div className="scenario-criteria">
                  <span className="criteria-label">Success Criteria:</span>
                  <span className="criteria-value">{scenario.criteria}</span>
                </div>
              </div>
            ))}
          </div>

          <div className="unit-tests-info">
            <h3>Unit Test Coverage</h3>
            <div className="test-coverage-grid">
              <div className="coverage-item">
                <span className="coverage-count">30</span>
                <span className="coverage-label">CircuitBreakerTests</span>
              </div>
              <div className="coverage-item">
                <span className="coverage-count">11</span>
                <span className="coverage-label">CircuitBreakerFactoryTests</span>
              </div>
              <div className="coverage-item">
                <span className="coverage-count">4</span>
                <span className="coverage-label">CircuitBreakerOptionsTests</span>
              </div>
            </div>
          </div>
        </section>

        {/* Glossary Section */}
        <section id="glossary" className="arch-section">
          <div className="section-header">
            <span className="section-icon">📚</span>
            <h2>Glossary</h2>
          </div>

          <div className="glossary-grid">
            {GLOSSARY.map((item) => (
              <div key={item.term} className="glossary-item">
                <dt className="glossary-term">{item.term}</dt>
                <dd className="glossary-definition">{item.definition}</dd>
              </div>
            ))}
          </div>
        </section>

        {/* Footer */}
        <footer className="arch-footer">
          <div className="footer-content">
            <span className="footer-version">Document Version: 2.0.0</span>
            <span className="footer-divider">|</span>
            <span className="footer-date">Last Updated: 2026-01-22</span>
          </div>
        </footer>
      </main>
    </div>
  );
}
