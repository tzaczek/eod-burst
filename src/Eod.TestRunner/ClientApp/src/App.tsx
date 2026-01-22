import { useState, useEffect, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import { TestScenario, TestResult, TestProgress, TestParameters } from './types';
import { Navigation, PageType } from './components/Navigation';
import { ArchitecturePage } from './pages/ArchitecturePage';
import { TestingPage } from './pages/TestingPage';
import './App.css';

function App() {
  const [currentPage, setCurrentPage] = useState<PageType>('architecture');
  const [scenarios, setScenarios] = useState<TestScenario[]>([]);
  const [results, setResults] = useState<Map<string, TestResult>>(new Map());
  const [progress, setProgress] = useState<Map<string, TestProgress>>(new Map());
  const [selectedScenario, setSelectedScenario] = useState<TestScenario | null>(null);
  const [showParameterModal, setShowParameterModal] = useState(false);
  const [isConnected, setIsConnected] = useState(false);

  // Fetch scenarios
  useEffect(() => {
    fetch('/api/scenarios')
      .then(res => res.json())
      .then(data => setScenarios(data))
      .catch(err => console.error('Failed to fetch scenarios:', err));
  }, []);

  // Setup SignalR connection
  useEffect(() => {
    const newConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/test')
      .withAutomaticReconnect()
      .build();

    newConnection.on('TestProgress', (prog: TestProgress) => {
      setProgress(prev => new Map(prev).set(prog.scenarioId, prog));
    });

    newConnection.on('TestCompleted', (result: TestResult) => {
      setResults(prev => new Map(prev).set(result.scenarioId, result));
      setProgress(prev => {
        const newProgress = new Map(prev);
        newProgress.delete(result.scenarioId);
        return newProgress;
      });
    });

    newConnection.onclose(() => setIsConnected(false));
    newConnection.onreconnected(() => setIsConnected(true));

    newConnection.start()
      .then(() => {
        setIsConnected(true);
      })
      .catch(err => console.error('SignalR connection failed:', err));

    return () => {
      newConnection.stop();
    };
  }, []);

  const runScenario = useCallback(async (scenario: TestScenario, params?: Partial<TestParameters>) => {
    try {
      const response = await fetch(`/api/scenarios/${scenario.id}/execute`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: params ? JSON.stringify(params) : null
      });
      
      if (!response.ok) {
        throw new Error('Failed to start test');
      }
      
      const data = await response.json();
      console.log('Test started:', data);
    } catch (err) {
      console.error('Failed to run scenario:', err);
    }
  }, []);

  const handleRunClick = (scenario: TestScenario) => {
    setSelectedScenario(scenario);
    setShowParameterModal(true);
  };

  const handleRunWithParams = (params: Partial<TestParameters>) => {
    if (selectedScenario) {
      runScenario(selectedScenario, params);
      setShowParameterModal(false);
      setSelectedScenario(null);
    }
  };

  const cancelTest = useCallback((scenarioId: string) => {
    fetch(`/api/scenarios/results/${scenarioId}/cancel`, { method: 'POST' })
      .catch(err => console.error('Failed to cancel test:', err));
  }, []);

  const completedResults = Array.from(results.values())
    .sort((a, b) => new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime());

  return (
    <div className="app">
      <Navigation 
        currentPage={currentPage}
        onPageChange={setCurrentPage}
        isConnected={isConnected}
      />
      
      <main className="main-content">
        {currentPage === 'architecture' && (
          <ArchitecturePage />
        )}
        
        {currentPage === 'testing' && (
          <TestingPage
            scenarios={scenarios}
            results={completedResults}
            progress={progress}
            onRunScenario={handleRunClick}
            onCancelTest={cancelTest}
            selectedScenario={selectedScenario}
            showParameterModal={showParameterModal}
            onRunWithParams={handleRunWithParams}
            onCloseModal={() => {
              setShowParameterModal(false);
              setSelectedScenario(null);
            }}
          />
        )}
      </main>
    </div>
  );
}

export default App;
