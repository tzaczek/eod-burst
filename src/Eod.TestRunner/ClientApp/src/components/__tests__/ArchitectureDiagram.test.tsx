import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { ArchitectureDiagram } from '../ArchitectureDiagram';

// Mock fetch
const mockFetch = vi.fn();
global.fetch = mockFetch;

describe('ArchitectureDiagram', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows loading state initially', () => {
    mockFetch.mockImplementation(() => new Promise(() => {})); // Never resolves
    
    render(<ArchitectureDiagram />);
    
    expect(screen.getByText('Loading system metrics...')).toBeInTheDocument();
  });

  it('displays system architecture title', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(createMockMetrics())
    });
    
    render(<ArchitectureDiagram />);
    
    await waitFor(() => {
      expect(screen.getByText('System Architecture')).toBeInTheDocument();
    });
  });

  it('displays all components', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(createMockMetrics())
    });
    
    render(<ArchitectureDiagram />);
    
    await waitFor(() => {
      expect(screen.getByText('FIX Simulator')).toBeInTheDocument();
      expect(screen.getByText('Ingestion')).toBeInTheDocument();
      expect(screen.getByText('Kafka')).toBeInTheDocument();
      expect(screen.getByText('Flash P&L')).toBeInTheDocument();
      expect(screen.getByText('Regulatory')).toBeInTheDocument();
      expect(screen.getByText('Redis')).toBeInTheDocument();
      expect(screen.getByText('SQL Server')).toBeInTheDocument();
    });
  });

  it('displays path labels', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(createMockMetrics())
    });
    
    render(<ArchitectureDiagram />);
    
    await waitFor(() => {
      expect(screen.getByText('HOT PATH')).toBeInTheDocument();
      expect(screen.getByText('COLD PATH')).toBeInTheDocument();
    });
  });

  it('displays metrics for ingestion', async () => {
    const metrics = createMockMetrics();
    metrics.ingestion.tradesIngested = 1000;
    
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(metrics)
    });
    
    render(<ArchitectureDiagram />);
    
    await waitFor(() => {
      expect(screen.getByText('1K')).toBeInTheDocument();
      expect(screen.getByText('trades')).toBeInTheDocument();
    });
  });

  it('displays LIVE indicator', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(createMockMetrics())
    });
    
    render(<ArchitectureDiagram />);
    
    await waitFor(() => {
      expect(screen.getByText('LIVE')).toBeInTheDocument();
    });
  });

  it('shows healthy status for up components', async () => {
    const metrics = createMockMetrics();
    metrics.ingestion.status = 'up';
    
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(metrics)
    });
    
    render(<ArchitectureDiagram />);
    
    await waitFor(() => {
      const upIndicators = screen.getAllByText('●');
      expect(upIndicators.length).toBeGreaterThan(0);
    });
  });

  it('formats large numbers correctly', async () => {
    const metrics = createMockMetrics();
    metrics.sqlServer.totalTrades = 1500000;
    
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(metrics)
    });
    
    render(<ArchitectureDiagram />);
    
    await waitFor(() => {
      expect(screen.getByText('1.5M')).toBeInTheDocument();
    });
  });

  it('handles fetch error gracefully', async () => {
    mockFetch.mockRejectedValue(new Error('Network error'));
    
    render(<ArchitectureDiagram />);
    
    await waitFor(() => {
      // Should show the diagram even without data
      expect(screen.getByText('System Architecture')).toBeInTheDocument();
    });
  });

  it('displays legend items', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(createMockMetrics())
    });
    
    render(<ArchitectureDiagram />);
    
    await waitFor(() => {
      expect(screen.getByText('Healthy')).toBeInTheDocument();
      expect(screen.getByText('Unavailable')).toBeInTheDocument();
      expect(screen.getByText('Low Latency')).toBeInTheDocument();
      expect(screen.getByText('High Throughput')).toBeInTheDocument();
    });
  });
});

function createMockMetrics() {
  return {
    ingestion: {
      tradesIngested: 100,
      messagesPerSecond: 10,
      status: 'up'
    },
    kafka: {
      messagesInTopic: 500,
      consumerLag: 50,
      status: 'up'
    },
    flashPnl: {
      tradesProcessed: 90,
      positionsInRedis: 10,
      status: 'up'
    },
    regulatory: {
      tradesInserted: 80,
      batchesPending: 0,
      status: 'up'
    },
    redis: {
      connectedClients: 5,
      keysCount: 20,
      status: 'up'
    },
    sqlServer: {
      totalTrades: 1000,
      status: 'up'
    }
  };
}
