import { TestScenario, TestResult, TestProgress, TestParameters } from '../types';
import { ScenarioCard } from '../components/ScenarioCard';
import { ResultsPanel } from '../components/ResultsPanel';
import { ParameterModal } from '../components/ParameterModal';
import { LiveMetrics } from '../components/LiveMetrics';
import { ArchitectureDiagram } from '../components/ArchitectureDiagram';
import './TestingPage.css';

interface TestingPageProps {
  scenarios: TestScenario[];
  results: TestResult[];
  progress: Map<string, TestProgress>;
  onRunScenario: (scenario: TestScenario) => void;
  onCancelTest: (scenarioId: string) => void;
  selectedScenario: TestScenario | null;
  showParameterModal: boolean;
  onRunWithParams: (params: Partial<TestParameters>) => void;
  onCloseModal: () => void;
}

export function TestingPage({
  scenarios,
  results,
  progress,
  onRunScenario,
  onCancelTest,
  selectedScenario,
  showParameterModal,
  onRunWithParams,
  onCloseModal,
}: TestingPageProps) {
  const runningTests = Array.from(progress.values());

  return (
    <div className="testing-page">
      <header className="testing-header">
        <div className="header-content">
          <h1>
            <span className="header-icon">🧪</span>
            Test Dashboard
          </h1>
          <p className="header-subtitle">
            Execute test scenarios against the EOD Burst system and monitor real-time performance metrics
          </p>
        </div>
      </header>

      <div className="testing-content">
        <ArchitectureDiagram />
        
        <LiveMetrics progress={runningTests} />
        
        <section className="scenarios-section">
          <div className="section-header">
            <h2>Test Scenarios</h2>
            <span className="scenario-count">{scenarios.length} available</span>
          </div>
          
          <div className="scenarios-grid">
            {scenarios.map((scenario, index) => (
              <ScenarioCard
                key={scenario.id}
                scenario={scenario}
                progress={progress.get(scenario.id)}
                onRun={() => onRunScenario(scenario)}
                onCancel={() => onCancelTest(scenario.id)}
                style={{ animationDelay: `${index * 0.05}s` }}
              />
            ))}
          </div>
        </section>

        <ResultsPanel 
          results={results}
          runningProgress={runningTests}
        />
      </div>

      {showParameterModal && selectedScenario && (
        <ParameterModal
          scenario={selectedScenario}
          onRun={onRunWithParams}
          onClose={onCloseModal}
        />
      )}
    </div>
  );
}
