import { useState, useEffect, useRef, useCallback } from 'react'
import { rulesApi, type SimulateResponse, type MediaType, type RuleConditionDto } from '../api/rules'
import './SandboxPage.css'

// ── Types ────────────────────────────────────────────────────────────────────

type MediaTypeFilter = MediaType | 'All'

interface FiltersState {
  mediaType: MediaTypeFilter
  lastWatchedDays: number   // 0 = disabled
  resolution: number        // 0 = any
  genre: string
}

const DEFAULT_FILTERS: FiltersState = {
  mediaType: 'Movie',
  lastWatchedDays: 180,
  resolution: 0,
  genre: '',
}

const RESOLUTION_OPTIONS = [
  { label: 'Any resolution', value: 0 },
  { label: '4K (2160p)',     value: 2160 },
  { label: '1080p',          value: 1080 },
  { label: '720p',           value: 720  },
  { label: '480p',           value: 480  },
]

const MEDIA_TYPE_OPTIONS: { label: string; value: MediaTypeFilter }[] = [
  { label: 'Movies',  value: 'Movie'  },
  { label: 'TV',      value: 'Series' },
]

// ── Helpers ──────────────────────────────────────────────────────────────────

function buildConditions(filters: FiltersState): RuleConditionDto[] {
  const conditions: RuleConditionDto[] = []

  if (filters.lastWatchedDays > 0) {
    const cutoff = new Date()
    cutoff.setDate(cutoff.getDate() - filters.lastWatchedDays)
    conditions.push({
      section: 0,
      logicalOperator: conditions.length === 0 ? null : 'And',
      field: 'LastWatched',
      comparator: 'Before',
      value: cutoff.toISOString(),
      valueType: 'Date',
    })
  }

  if (filters.resolution > 0) {
    conditions.push({
      section: 0,
      logicalOperator: conditions.length === 0 ? null : 'And',
      field: 'ResolutionHeight',
      comparator: 'Equals',
      value: String(filters.resolution),
      valueType: 'Number',
    })
  }

  if (filters.genre.trim()) {
    conditions.push({
      section: 0,
      logicalOperator: conditions.length === 0 ? null : 'And',
      field: 'Genre',
      comparator: 'Contains',
      value: filters.genre.trim(),
      valueType: 'TextList',
    })
  }

  // Re-index logicalOperator: first condition in section must be null
  return conditions.map((c, i) => ({ ...c, logicalOperator: i === 0 ? null : 'And' }))
}

function sizeLabel(gb: number): string {
  if (gb < 1) return `${(gb * 1024).toFixed(0)} MB`
  return `${gb.toFixed(1)} GB`
}

function sizeBadgeClass(gb: number): string {
  if (gb > 500) return 'sandbox__badge-value--red'
  if (gb > 100) return 'sandbox__badge-value--yellow'
  return 'sandbox__badge-value--green'
}

// ── Donut chart (CSS conic-gradient) ─────────────────────────────────────────

function DonutChart({ breakdown }: { breakdown: Record<string, number> }) {
  const entries = Object.entries(breakdown)
  const total = entries.reduce((s, [, v]) => s + v, 0)
  if (total === 0) return null

  // Assign colors per category
  const COLORS: Record<string, string> = {
    Movie:   'var(--accent)',
    Series:  'var(--success)',
    Season:  'var(--warning)',
    Episode: 'var(--info)',
  }
  const DOT_CLASS: Record<string, string> = {
    Movie:   'sandbox__legend-dot--movie',
    Series:  'sandbox__legend-dot--series',
    Season:  'sandbox__legend-dot--other',
    Episode: 'sandbox__legend-dot--other',
  }

  // Build conic-gradient stops
  let acc = 0
  const stops = entries.map(([key, val]) => {
    const pct = (val / total) * 100
    const color = COLORS[key] ?? 'var(--warning)'
    const stop = `${color} ${acc.toFixed(2)}% ${(acc + pct).toFixed(2)}%`
    acc += pct
    return stop
  })
  const gradient = `conic-gradient(${stops.join(', ')})`

  return (
    <div className="sandbox__donut-section">
      <div className="sandbox__donut-wrap">
        <div className="sandbox__donut-chart" style={{ background: gradient }} />
        <div className="sandbox__donut-hole" />
      </div>
      <div className="sandbox__donut-legend">
        {entries.map(([key, val]) => (
          <div key={key} className="sandbox__legend-item">
            <span className={`sandbox__legend-dot ${DOT_CLASS[key] ?? 'sandbox__legend-dot--other'}`} />
            <span>{key}</span>
            <span className="sandbox__legend-gb">{sizeLabel(val)}</span>
          </div>
        ))}
      </div>
    </div>
  )
}

// ── Main component ────────────────────────────────────────────────────────────

export function SandboxPage() {
  const [filters, setFilters] = useState<FiltersState>(DEFAULT_FILTERS)
  const [result, setResult] = useState<SimulateResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const runSimulate = useCallback(async (f: FiltersState) => {
    const conditions = buildConditions(f)
    if (conditions.length === 0) {
      setResult(null)
      setError(null)
      return
    }

    const mediaType: MediaType = f.mediaType === 'All' ? 'Movie' : f.mediaType

    setLoading(true)
    setError(null)
    try {
      const data = await rulesApi.simulate({ mediaType, conditions })
      setResult(data)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Simulation failed')
      setResult(null)
    } finally {
      setLoading(false)
    }
  }, [])

  // Debounced trigger on filter change
  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(() => runSimulate(filters), 500)
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current) }
  }, [filters, runSimulate])

  function setF<K extends keyof FiltersState>(key: K, value: FiltersState[K]) {
    setFilters(prev => ({ ...prev, [key]: value }))
  }

  const hasConditions = buildConditions(filters).length > 0

  return (
    <div className="sandbox">
      <div className="sandbox__header">
        <h1>Sandbox Simulator</h1>
        <p>Forecast how much space your rules would reclaim — without touching any data.</p>
      </div>

      <div className="sandbox__body">
        {/* ── Sidebar ─────────────────────────────────────────────────── */}
        <aside className="sandbox__sidebar">
          <div>
            <h2>Media Type</h2>
            <div className="sandbox__radio-group">
              {MEDIA_TYPE_OPTIONS.map(opt => (
                <button
                  key={opt.value}
                  className={filters.mediaType === opt.value ? 'active' : ''}
                  onClick={() => setF('mediaType', opt.value)}
                >
                  {opt.label}
                </button>
              ))}
            </div>
          </div>

          <div className="sandbox__field">
            <label>Last watched older than</label>
            <div className="sandbox__slider-row">
              <input
                type="range"
                className="sandbox__slider"
                min={0}
                max={1825}
                step={30}
                value={filters.lastWatchedDays}
                onChange={e => setF('lastWatchedDays', Number(e.target.value))}
              />
              <span className="sandbox__slider-val">
                {filters.lastWatchedDays === 0 ? 'Off' : `${filters.lastWatchedDays}d`}
              </span>
            </div>
            <span className="field-hint">
              {filters.lastWatchedDays === 0
                ? 'Drag slider to enable'
                : `Items not watched in the last ${filters.lastWatchedDays} days`}
            </span>
          </div>

          <div className="sandbox__field">
            <label>Resolution</label>
            <select
              className="sandbox__select"
              value={filters.resolution}
              onChange={e => setF('resolution', Number(e.target.value))}
            >
              {RESOLUTION_OPTIONS.map(o => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>
          </div>

          <div className="sandbox__field">
            <label>Genre contains</label>
            <input
              type="text"
              className="sandbox__input"
              placeholder="e.g. Action"
              value={filters.genre}
              onChange={e => setF('genre', e.target.value)}
            />
          </div>
        </aside>

        {/* ── Results panel ───────────────────────────────────────────── */}
        <main className="sandbox__panel">
          {!hasConditions && !loading && (
            <div className="sandbox__empty">
              <div className="sandbox__empty-icon">🧪</div>
              <p>Set at least one filter on the left to run a simulation.</p>
            </div>
          )}

          {hasConditions && loading && (
            <div className="sandbox__loading">
              <div className="sandbox__spinner" />
              Running simulation…
            </div>
          )}

          {error && (
            <div className="sandbox__note">{error}</div>
          )}

          {result && !loading && (
            <>
              {result.note && (
                <div className="sandbox__note">{result.note}</div>
              )}

              {result.matchedCount > 0 && (
                <>
                  {/* Stats badges */}
                  <div className="sandbox__stats">
                    <div className="sandbox__badge">
                      <span className="sandbox__badge-label">Items matched</span>
                      <span className="sandbox__badge-value">{result.matchedCount}</span>
                    </div>
                    <div className="sandbox__badge">
                      <span className="sandbox__badge-label">Space reclaimable</span>
                      <span className={`sandbox__badge-value ${sizeBadgeClass(result.totalReclaimedGb)}`}>
                        {sizeLabel(result.totalReclaimedGb)}
                      </span>
                    </div>
                  </div>

                  {/* Donut chart */}
                  {Object.keys(result.categoryBreakdown).length > 0 && (
                    <DonutChart breakdown={result.categoryBreakdown} />
                  )}

                  {/* Library breakdown */}
                  {result.libraryBreakdown.length > 0 && (
                    <div className="sandbox__breakdown">
                      <div className="sandbox__breakdown-header">By connection</div>
                      {result.libraryBreakdown.map(row => (
                        <div key={row.library} className="sandbox__breakdown-row">
                          <span className="sandbox__breakdown-name">{row.library}</span>
                          <span className="sandbox__breakdown-count">{row.matchedCount} items</span>
                          <span className="sandbox__breakdown-gb">{sizeLabel(row.reclaimedGb)}</span>
                        </div>
                      ))}
                    </div>
                  )}

                  {/* Sample titles */}
                  {result.sampleTitles.length > 0 && (
                    <div className="sandbox__samples">
                      <div className="sandbox__samples-header">
                        Sample titles ({result.sampleTitles.length} of {result.matchedCount})
                      </div>
                      <ul className="sandbox__samples-list">
                        {result.sampleTitles.map(title => (
                          <li key={title}>{title}</li>
                        ))}
                      </ul>
                    </div>
                  )}
                </>
              )}
            </>
          )}
        </main>
      </div>
    </div>
  )
}
