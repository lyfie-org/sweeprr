import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach } from 'vitest'
import { RulesPage } from '../RulesPage'
import { rulesApi } from '../../api/rules'
import { ToastProvider } from '../../components/ui/ToastContext'

// Mock react-router-dom
vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
}))

// Mock rulesApi module
vi.mock('../../api/rules', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/rules')>()
  return {
    ...actual,
    rulesApi: {
      getAll: vi.fn(),
      scan: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      getFieldsMeta: vi.fn(),
    },
  }
})

// Mock the RuleGroupEditor subcomponent since we're integration testing RulesPage's core states
vi.mock('../../components/rules', () => ({
  RuleGroupEditor: ({ onSaved }: { onSaved: () => void }) => (
    <div data-testid="rule-editor">
      <button onClick={onSaved}>Save</button>
    </div>
  ),
}))

describe('RulesPage Component', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders loading state initially', async () => {
    vi.mocked(rulesApi.getAll).mockReturnValue(new Promise(() => {}))
    render(
      <ToastProvider>
        <RulesPage />
      </ToastProvider>
    )
    expect(screen.getByRole('status')).toBeInTheDocument() // spinner has role="status"
  })

  it('renders empty state when no rule groups are fetched', async () => {
    vi.mocked(rulesApi.getAll).mockResolvedValue([])

    render(
      <ToastProvider>
        <RulesPage />
      </ToastProvider>
    )

    await waitFor(() => {
      expect(screen.queryByRole('status')).not.toBeInTheDocument()
    })

    expect(screen.getByText('No rule groups yet')).toBeInTheDocument()
    expect(screen.getByText('Create First Rule Group')).toBeInTheDocument()
  })

  it('renders list of rule groups', async () => {
    vi.mocked(rulesApi.getAll).mockResolvedValue([
      {
        id: 1,
        name: 'My Rule Group',
        description: 'Sweep description text',
        mediaType: 'Movie',
        isEnabled: true,
        cronOverride: null,
        action: 'DeleteAndUnmonitor',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        conditions: [],
      },
    ])

    render(
      <ToastProvider>
        <RulesPage />
      </ToastProvider>
    )

    await waitFor(() => {
      expect(screen.getByText('My Rule Group')).toBeInTheDocument()
    })

    expect(screen.getByText('Sweep description text')).toBeInTheDocument()
    expect(screen.getByText('Delete & Unmonitor')).toBeInTheDocument()
  })

  it('opens create editor when clicking new rule group', async () => {
    vi.mocked(rulesApi.getAll).mockResolvedValue([])

    render(
      <ToastProvider>
        <RulesPage />
      </ToastProvider>
    )

    await waitFor(() => {
      expect(screen.getByText('No rule groups yet')).toBeInTheDocument()
    })

    fireEvent.click(screen.getByText('Create First Rule Group'))

    expect(screen.getByTestId('rule-editor')).toBeInTheDocument()
  })
})
