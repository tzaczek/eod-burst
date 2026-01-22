export type TestType = 
  | 'HealthCheck' 
  | 'Throughput' 
  | 'Latency' 
  | 'EndToEnd' 
  | 'BurstMode' 
  | 'DataIntegrity';

export type TestStatus = 
  | 'Pending' 
  | 'Running' 
  | 'Passed' 
  | 'Failed' 
  | 'Cancelled';

export interface TestParameters {
  tradeCount: number;
  tradesPerSecond: number;
  burstMultiplier: number;
  burstDurationSeconds: number;
  symbols: string[];
  traderIds: string[];
  timeoutSeconds: number;
  warmupSeconds: number;
  expectedLatencyMs: number;
  acceptableErrorRate: number;
  ingestionUrl?: string;
  flashPnlUrl?: string;
  regulatoryUrl?: string;
}

export interface TestScenario {
  id: string;
  name: string;
  description: string;
  type: TestType;
  parameters: TestParameters;
}

export interface TestStep {
  name: string;
  status: TestStatus;
  message?: string;
  startedAt?: string;
  completedAt?: string;
  duration?: string;
}

export interface TestResult {
  scenarioId: string;
  scenarioName: string;
  status: TestStatus;
  startedAt: string;
  completedAt?: string;
  duration?: string;
  tradesGenerated: number;
  tradesProcessedByPnl: number;
  tradesInsertedToSql: number;
  averageLatencyMs: number;
  p95LatencyMs: number;
  p99LatencyMs: number;
  throughputPerSecond: number;
  errorCount: number;
  steps: TestStep[];
  errors: string[];
  metadata: Record<string, unknown>;
}

export interface TestProgress {
  scenarioId: string;
  currentStep: string;
  percentComplete: number;
  tradesGenerated: number;
  tradesProcessed: number;
  currentThroughput: number;
  message?: string;
}
