import { useEffect, useState, useRef } from 'react'
import {
  Article,
  DownloadSimple,
  ArrowClockwise,
  ArrowLeft,
  ArrowRight,
  CaretRight,
  CaretDown,
  MagnifyingGlass,
} from '@phosphor-icons/react'
import { logsApi, type LogEntry } from '../api/logs'
import { Card, CardBody, Badge, Button, Input, Toggle, Spinner, useToast } from '../components/ui'
import './LogsPage.css'

type BadgeVariant = 'success' | 'warning' | 'danger' | 'info' | 'neutral' | 'accent'

const CATEGORY_VARIANTS: Record<string, BadgeVariant> = {
  Sweep: 'accent',
  Connection: 'info',
  Rule: 'neutral',
  Auth: 'warning',
  System: 'neutral',
}

export function LogsPage() {
  const { toast } = useToast()
  
  // ── State variables ────────────────────────────────────────────────────────
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [level, setLevel] = useState('')
  const [category, setCategory] = useState('')
  const [searchVal, setSearchVal] = useState('')
  const [search, setSearch] = useState('')
  const [autoScroll, setAutoScroll] = useState(true)
  const [autoRefresh, setAutoRefresh] = useState(false)
  const [loading, setLoading] = useState(true)
  const [expandedIndices, setExpandedIndices] = useState<Set<number>>(new Set())

  const consoleRef = useRef<HTMLDivElement>(null)
  const pageSize = 50

  // ── Debounce search input ──────────────────────────────────────────────────
  useEffect(() => {
    const handler = setTimeout(() => {
      setSearch(searchVal)
      setPage(1)
    }, 300)
    return () => clearTimeout(handler)
  }, [searchVal])

  // ── Fetch logs from API ────────────────────────────────────────────────────
  const fetchLogs = async (showSpinner = true) => {
    if (showSpinner) setLoading(true)
    try {
      const data = await logsApi.getLogs({
        level: level || undefined,
        category: category || undefined,
        search: search || undefined,
        page,
        pageSize,
      })
      setLogs(data.items)
      setTotalCount(data.totalCount)
    } catch (err: any) {
      toast({
        type: 'error',
        title: 'Failed to Load Logs',
        message: err.message || 'An error occurred while loading log entries.',
      })
    } finally {
      if (showSpinner) setLoading(false)
    }
  }

  // Fetch when filters or page changes
  useEffect(() => {
    fetchLogs(true)
  }, [page, level, category, search])

  // Auto-refresh timer
  useEffect(() => {
    if (!autoRefresh) return

    const timer = setInterval(() => {
      fetchLogs(false)
    }, 5000)

    return () => clearInterval(timer)
  }, [page, level, category, search, autoRefresh])

  // Auto-scroll console to bottom
  useEffect(() => {
    if (autoScroll && consoleRef.current) {
      consoleRef.current.scrollTop = consoleRef.current.scrollHeight
    }
  }, [logs, autoScroll])

  // ── Toggle exception display ───────────────────────────────────────────────
  const toggleException = (index: number) => {
    setExpandedIndices(prev => {
      const next = new Set(prev)
      if (next.has(index)) {
        next.delete(index)
      } else {
        next.add(index)
      }
      return next
    })
  }

  // ── Authenticated file download ────────────────────────────────────────────
  const handleDownload = async () => {
    try {
      const token = localStorage.getItem('sweeprr_token')
      const res = await fetch('/api/logs/download', {
        headers: {
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
      })
      if (!res.ok) throw new Error('Download failed')

      const blob = await res.blob()
      const url = window.URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url

      // Extract filename from content-disposition header if possible
      const disposition = res.headers.get('content-disposition')
      let filename = 'sweeprr.log'
      if (disposition && disposition.indexOf('attachment') !== -1) {
        const filenameRegex = /filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/
        const matches = filenameRegex.exec(disposition)
        if (matches != null && matches[1]) {
          filename = matches[1].replace(/['"]/g, '')
        }
      }

      a.download = filename
      document.body.appendChild(a)
      a.click()
      a.remove()
      window.URL.revokeObjectURL(url)

      toast({
        type: 'success',
        title: 'Download Started',
        message: `Successfully downloaded ${filename}`,
      })
    } catch (err: any) {
      toast({
        type: 'error',
        title: 'Download Failed',
        message: err.message || 'Could not download active log file.',
      })
    }
  }

  const totalPages = Math.ceil(totalCount / pageSize)

  return (
    <div className="logs-page">
      <div className="logs-header">
        <div>
          <h1 className="logs-title">System Logs</h1>
          <p className="logs-subtitle">View and filter detailed application diagnostics</p>
        </div>
        <div style={{ display: 'flex', gap: 'var(--space-2)' }}>
          <Button
            variant="secondary"
            onClick={() => fetchLogs(true)}
            iconLeft={<ArrowClockwise size={16} />}
          >
            Refresh
          </Button>
          <Button
            variant="secondary"
            onClick={handleDownload}
            iconLeft={<DownloadSimple size={16} />}
          >
            Download
          </Button>
        </div>
      </div>

      <Card>
        <CardBody>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-4)' }}>
          {/* Filters and Search toolbar */}
          <div className="logs-toolbar">
            <div className="logs-filters">
              <div className="logs-filter-group" style={{ flex: 1, minWidth: '200px', maxWidth: '300px' }}>
                <span className="logs-filter-label">Search Logs</span>
                <Input
                  placeholder="Search messages..."
                  value={searchVal}
                  onChange={e => setSearchVal(e.target.value)}
                  iconLeft={<MagnifyingGlass size={16} />}
                />
              </div>

              <div className="logs-filter-group">
                <span className="logs-filter-label">Severity</span>
                <select
                  className="logs-select"
                  value={level}
                  onChange={e => {
                    setLevel(e.target.value)
                    setPage(1)
                  }}
                >
                  <option value="">All Levels</option>
                  <option value="Debug">Debug</option>
                  <option value="Information">Info</option>
                  <option value="Warning">Warning</option>
                  <option value="Error">Error</option>
                  <option value="Fatal">Fatal</option>
                </select>
              </div>

              <div className="logs-filter-group">
                <span className="logs-filter-label">Category</span>
                <select
                  className="logs-select"
                  value={category}
                  onChange={e => {
                    setCategory(e.target.value)
                    setPage(1)
                  }}
                >
                  <option value="">All Categories</option>
                  <option value="Sweep">Sweep</option>
                  <option value="Connection">Connection</option>
                  <option value="Rule">Rule</option>
                  <option value="Auth">Auth</option>
                  <option value="System">System</option>
                </select>
              </div>
            </div>

            <div className="logs-controls">
              <div className="logs-toggle-control">
                <Toggle
                  checked={autoScroll}
                  onChange={setAutoScroll}
                  id="auto-scroll-toggle"
                />
                <label htmlFor="auto-scroll-toggle" className="logs-toggle-label">
                  Auto-Scroll
                </label>
              </div>

              <div className="logs-toggle-control">
                <Toggle
                  checked={autoRefresh}
                  onChange={setAutoRefresh}
                  id="auto-refresh-toggle"
                />
                <label htmlFor="auto-refresh-toggle" className="logs-toggle-label">
                  Auto-Refresh (5s)
                </label>
              </div>
            </div>
          </div>

          {/* Console Output Area */}
          <div
            className={`logs-console${loading ? ' logs-console--loading' : ''}`}
            ref={consoleRef}
          >
            {loading ? (
              <Spinner size="lg" />
            ) : logs.length === 0 ? (
              <div className="logs-empty">
                <Article size={40} weight="duotone" />
                <p style={{ margin: 0 }}>No matching log entries found.</p>
              </div>
            ) : (
              logs.map((log, idx) => {
                const isExpanded = expandedIndices.has(idx)
                
                return (
                  <div key={idx} className="log-row">
                    <div className="log-meta">
                      <span className="log-time">
                        {new Date(log.timestamp).toLocaleTimeString(undefined, {
                          hour12: false,
                          hour: '2-digit',
                          minute: '2-digit',
                          second: '2-digit',
                          fractionalSecondDigits: 3,
                        })}
                      </span>
                      <span className={`log-level log-level--${log.level.toLowerCase()}`}>
                        [{log.level.substring(0, 3)}]
                      </span>
                      <Badge variant={CATEGORY_VARIANTS[log.category] || 'neutral'} size="sm">
                        {log.category}
                      </Badge>
                      <span className="log-context" title={log.sourceContext}>
                        ({log.sourceContext.split('.').pop()})
                      </span>
                    </div>

                    <div className="log-message-wrap">
                      <span className="log-message">{log.message}</span>
                      {log.exception && (
                        <button
                          className="log-expander"
                          onClick={() => toggleException(idx)}
                          aria-label={isExpanded ? 'Collapse stack trace' : 'Expand stack trace'}
                        >
                          {isExpanded ? <CaretDown size={14} /> : <CaretRight size={14} />}
                        </button>
                      )}
                    </div>

                    {log.exception && isExpanded && (
                      <pre className="log-exception">{log.exception}</pre>
                    )}
                  </div>
                )
              })
            )}
          </div>

          {/* Pagination Controls */}
          {!loading && totalPages > 1 && (
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 'var(--space-2)' }}>
              <div style={{ fontSize: 'var(--text-sm)', color: 'var(--text-secondary)' }}>
                Showing {((page - 1) * pageSize) + 1} to {Math.min(page * pageSize, totalCount)} of {totalCount} logs
              </div>
              <div style={{ display: 'flex', gap: 'var(--space-2)' }}>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => setPage(p => Math.max(1, p - 1))}
                  disabled={page === 1}
                  iconLeft={<ArrowLeft size={14} />}
                >
                  Previous
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                  disabled={page === totalPages}
                  iconRight={<ArrowRight size={14} />}
                >
                  Next
                </Button>
              </div>
            </div>
          )}
          </div>
        </CardBody>
      </Card>
    </div>
  )
}
