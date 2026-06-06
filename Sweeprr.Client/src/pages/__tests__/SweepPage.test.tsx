import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach } from 'vitest'
import { SweepPage } from '../SweepPage'
import { sweepApi } from '../../api/sweep'
import { settingsApi } from '../../api/settings'
import { ToastProvider } from '../../components/ui/ToastContext'

// Mock react-router-dom
vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
}))

// Mock sweepApi module
vi.mock('../../api/sweep', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/sweep')>()
  return {
    ...actual,
    sweepApi: {
      getAll: vi.fn(),
      getSummary: vi.fn(),
      getById: vi.fn(),
      approve: vi.fn(),
      ignore: vi.fn(),
      skip: vi.fn(),
      execute: vi.fn(),
      run: vi.fn(),
    },
  }
})

// Mock settingsApi module
vi.mock('../../api/settings', () => ({
  settingsApi: {
    get: vi.fn(),
    patch: vi.fn(),
  },
}))

describe('SweepPage Component', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(settingsApi.get).mockResolvedValue({
      instanceName: 'Test Sweeprr',
      globalDryRun: true,
      maxItemsPerRun: 20,
      maxGbPerRun: 50.0,
      pessimisticSizeGb: 5.0,
      libraryPercentCap: 50.0,
      overBroadMatchPct: 80.0,
      defaultCron: '0 3 * * *',
    })
    vi.mocked(sweepApi.getSummary).mockResolvedValue({
      pendingCount: 2,
      approvedCount: 0,
      pendingBytes: 2147483648, // 2 GB
      approvedBytes: 0,
    })
    vi.mocked(sweepApi.getAll).mockResolvedValue({
      items: [
        {
          id: 1,
          ruleGroupId: 1,
          ruleGroupName: 'Movies Rule Group',
          mediaServerItemId: 'jf-001',
          title: 'Movie One',
          mediaType: 'Movie',
          sizeBytes: 1073741824, // 1 GB
          matchedRuleSummary: 'Watched in last 30 days',
          status: 'Pending',
          arrInstanceId: 1,
          tmdbId: '101',
          tvdbId: null,
          imdbId: null,
          flaggedAt: new Date().toISOString(),
          sweptAt: null,
          skippedReason: null,
          seasonNumber: null,
        },
      ],
      total: 1,
      page: 1,
      pageSize: 50,
    })
  })

  it('renders dry-run banner when globalDryRun is active', async () => {
    render(
      <ToastProvider>
        <SweepPage />
      </ToastProvider>
    )

    await waitFor(() => {
      expect(screen.getByText(/Dry-Run Mode is Active/i)).toBeInTheDocument()
    })
  })

  it('renders sweep stats summary bar', async () => {
    render(
      <ToastProvider>
        <SweepPage />
      </ToastProvider>
    )

    await waitFor(() => {
      expect(screen.getByText('Reclaimable Space')).toBeInTheDocument()
      expect(screen.getByText('2.00 GB')).toBeInTheDocument() // pendingBytes formatted
    })
  })

  it('renders tables of sweep queue items', async () => {
    render(
      <ToastProvider>
        <SweepPage />
      </ToastProvider>
    )

    await waitFor(() => {
      expect(screen.getByText('Movie One')).toBeInTheDocument()
      expect(screen.getByText('Watched in last 30 days')).toBeInTheDocument()
      expect(screen.getByText('1 GB')).toBeInTheDocument() // sizeBytes formatted
    })
  })

  it('shows confirmation modal when sweeping an item', async () => {
    render(
      <ToastProvider>
        <SweepPage />
      </ToastProvider>
    )

    await waitFor(() => {
      expect(screen.getByText('Movie One')).toBeInTheDocument()
    })

    const sweepBtn = screen.getByRole('button', { name: 'Sweep' })
    fireEvent.click(sweepBtn)

    expect(screen.getByText(/Dry-Run Mode is ON/i)).toBeInTheDocument()
  })
})
