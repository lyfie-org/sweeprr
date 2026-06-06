import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Broom,
  Warning,
  CheckCircle,
  Play,
  ArrowLeft,
  ArrowRight,
  X,
  FilmSlate,
  Television,
  VideoCamera,
  Info,
} from '@phosphor-icons/react'
import {
  sweepApi,
  needsReview,
  formatBytes,
  formatGb,
  STATUS_LABELS,
  STATUS_VARIANTS,
  type SweepItem,
  type SweepItemStatus,
  type SweepSummary,
  type ExecuteSweepResult,
} from '../api/sweep'
import { settingsApi } from '../api/settings'
import {
  Badge,
  Button,
  Input,
  Modal,
  Spinner,
  useToast,
} from '../components/ui'
import './SweepPage.css'

const FILTER_STATUSES: (SweepItemStatus | 'All')[] = [
  'Pending',
  'Approved',
  'Ignored',
  'Swept',
  'Failed',
  'All',
]

const MEDIA_ICONS: Record<string, React.ReactNode> = {
  Movie:   <FilmSlate size={16} weight="duotone" />,
  Series:  <Television size={16} weight="duotone" />,
  Season:  <Television size={16} weight="duotone" />,
  Episode: <VideoCamera size={16} weight="duotone" />,
}

export function SweepPage() {
  const navigate = useNavigate()
  const { toast } = useToast()

  // ── States ──────────────────────────────────────────────────────────────────
  const [isGlobalDryRun, setIsGlobalDryRun] = useState(true)
  const [summary, setSummary] = useState<SweepSummary | null>(null)
  const [items, setItems] = useState<SweepItem[]>([])
  const [totalItems, setTotalItems] = useState(0)
  const [currentPage, setCurrentPage] = useState(1)
  const [statusFilter, setStatusFilter] = useState<SweepItemStatus | 'All'>('Pending')
  const [searchQuery, setSearchQuery] = useState('')
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set())

  // Loading and scanning indicators
  const [loading, setLoading] = useState(false)
  const [scanning, setScanning] = useState(false)
  const [executing, setExecuting] = useState(false)

  // Execution result and failsafe banners
  const [executeResult, setExecuteResult] = useState<ExecuteSweepResult | null>(null)
  const [showFailsafeBanner, setShowFailsafeBanner] = useState(false)
  const [failsafeSkippedCount, setFailsafeSkippedCount] = useState(0)

  // Confirmation modal states
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [confirmAction, setConfirmAction] = useState<'sweep' | 'bulk-sweep' | 'bulk-ignore' | null>(null)
  const [confirmTargetIds, setConfirmTargetIds] = useState<number[]>([])

  // Popover state for per-row Ignore
  const [ignorePopoverItemId, setIgnorePopoverItemId] = useState<number | null>(null)
  const [ignoreCreateExclusion, setIgnoreCreateExclusion] = useState(false)

  const pageSize = 50

  // ── Fetching logic ─────────────────────────────────────────────────────────
  const fetchSummary = useCallback(async () => {
    try {
      const data = await sweepApi.getSummary()
      setSummary(data)
    } catch (err: any) {
      console.error('Failed to fetch sweep summary:', err)
    }
  }, [])

  const fetchItems = useCallback(async () => {
    setLoading(true)
    try {
      const data = await sweepApi.getAll({
        status: statusFilter,
        page: currentPage,
        pageSize,
      })
      setItems(data.items)
      setTotalItems(data.total)
    } catch (err: any) {
      toast({
        type: 'error',
        title: 'Error Fetching Items',
        message: err.message || 'An error occurred while loading the sweep queue.'
      })
    } finally {
      setLoading(false)
    }
  }, [statusFilter, currentPage, toast])

  // Fetch initial configuration settings
  useEffect(() => {
    settingsApi.get()
      .then(settings => {
        setIsGlobalDryRun(settings.globalDryRun)
      })
      .catch(err => {
        console.error('Failed to load global dry-run configuration:', err)
      })
  }, [])

  // Refetch items when status filter or page changes
  useEffect(() => {
    fetchItems()
  }, [fetchItems])

  // Refetch summary bar stats on mount and after actions
  useEffect(() => {
    fetchSummary()
  }, [fetchSummary])

  // Reset selected state when filter changes
  useEffect(() => {
    setSelectedIds(new Set())
  }, [statusFilter])

  // ── Action Handlers ────────────────────────────────────────────────────────

  // Trigger manual rule evaluation scan
  const handleRunScan = async () => {
    setScanning(true)
    setExecuteResult(null)
    try {
      const result = await sweepApi.run()
      const totalFlagged = result.results.reduce((acc, r) => acc + (r.itemsFlagged ?? 0), 0)
      
      toast({
        type: 'success',
        title: 'Scan Complete',
        message: `Found ${totalFlagged} item(s) matching enabled rules.`
      })
      
      setCurrentPage(1)
      fetchSummary()
      fetchItems()
    } catch (err: any) {
      toast({
        type: 'error',
        title: 'Scan Failed',
        message: err.error || err.message || 'An error occurred while running the scan.'
      })
    } finally {
      setScanning(false)
    }
  }

  // Handle single ignore click confirm
  const handleSingleIgnore = async (itemId: number, createExclusion: boolean) => {
    try {
      await sweepApi.ignore(itemId, createExclusion)
      toast({
        type: 'success',
        title: 'Item Ignored',
        message: 'Item removed from the queue.'
      })
      setIgnorePopoverItemId(null)
      fetchSummary()
      fetchItems()
    } catch (err: any) {
      toast({
        type: 'error',
        title: 'Ignore Failed',
        message: err.message || 'An error occurred.'
      })
    }
  }

  // Handle confirm actions for Sweep / Bulk Sweep / Bulk Ignore
  const handleConfirmAction = async () => {
    if (confirmAction === 'sweep' || confirmAction === 'bulk-sweep') {
      setExecuting(true)
      try {
        // Approve first
        await Promise.all(confirmTargetIds.map(id => sweepApi.approve(id)))
        
        // Execute sweep
        const result = await sweepApi.execute({ itemIds: confirmTargetIds })
        setExecuteResult(result)
        
        if (result.itemsSkippedByFailsafe > 0) {
          setShowFailsafeBanner(true)
          setFailsafeSkippedCount(result.itemsSkippedByFailsafe)
        }
        
        if (result.wasDryRun) {
          toast({
            type: 'info',
            title: 'Dry-Run Complete',
            message: `Simulated sweep of ${result.itemsSwept} item(s).`
          })
        } else {
          toast({
            type: 'success',
            title: 'Sweep Complete',
            message: `Successfully swept ${result.itemsSwept} item(s).`
          })
        }
        
        setSelectedIds(new Set())
        fetchSummary()
        fetchItems()
      } catch (err: any) {
        toast({
          type: 'error',
          title: 'Sweep Failed',
          message: err.message || 'An error occurred during execution.'
        })
      } finally {
        setExecuting(false)
        setConfirmOpen(false)
        setConfirmAction(null)
        setConfirmTargetIds([])
      }
    } else if (confirmAction === 'bulk-ignore') {
      setExecuting(true)
      try {
        await Promise.all(confirmTargetIds.map(id => sweepApi.ignore(id, ignoreCreateExclusion)))
        toast({
          type: 'success',
          title: 'Items Ignored',
          message: `Successfully ignored ${confirmTargetIds.length} item(s).`
        })
        setSelectedIds(new Set())
        fetchSummary()
        fetchItems()
      } catch (err: any) {
        toast({
          type: 'error',
          title: 'Ignore Failed',
          message: err.message || 'An error occurred.'
        })
      } finally {
        setExecuting(false)
        setConfirmOpen(false)
        setConfirmAction(null)
        setConfirmTargetIds([])
      }
    }
  }

  // ── Render ──────────────────────────────────────────────────────────────────

  const filteredItems = items.filter(item =>
    item.title.toLowerCase().includes(searchQuery.toLowerCase())
  )

  const totalPages = Math.ceil(totalItems / pageSize)

  return (
    <div className="sweep-page">
      {/* Dry-run status banner */}
      {isGlobalDryRun && (
        <div className="dry-run-banner">
          <Warning className="dry-run-banner__icon" size={18} weight="fill" />
          <div className="dry-run-banner__text">
            <strong>Dry-Run Mode is Active.</strong> Sweeps will only simulate file deletion and Radarr/Sonarr unmonitoring.
          </div>
          <button
            className="dry-run-banner__link"
            onClick={() => navigate('/settings')}
          >
            Configure Settings
          </button>
        </div>
      )}

      {/* Failsafe warning banner */}
      {showFailsafeBanner && (
        <div className="failsafe-banner">
          <Warning className="failsafe-banner__icon" size={18} weight="fill" />
          <div className="failsafe-banner__body">
            <div className="failsafe-banner__title">Failsafe Limit Reached</div>
            <div className="failsafe-banner__msg">
              Sweep run halted because a safety limit was hit. {failsafeSkippedCount} item(s) were skipped to protect your library. Adjust caps in <span style={{ textDecoration: 'underline', cursor: 'pointer' }} onClick={() => navigate('/settings')}>Settings</span> to continue.
            </div>
          </div>
          <button
            className="failsafe-banner__dismiss"
            onClick={() => setShowFailsafeBanner(false)}
            aria-label="Dismiss"
          >
            <X size={16} weight="bold" />
          </button>
        </div>
      )}

      {/* Execute Result Banner */}
      {executeResult && (
        <div className={`execute-result ${executeResult.wasDryRun ? 'execute-result--dryrun' : 'execute-result--success'}`}>
          {executeResult.wasDryRun ? (
            <>
              <CheckCircle className="failsafe-banner__icon" size={18} weight="fill" />
              <div>
                Dry-run completed successfully! Simulated sweeping {executeResult.itemsSwept} item(s).
              </div>
            </>
          ) : (
            <>
              <CheckCircle className="failsafe-banner__icon" size={18} weight="fill" />
              <div>
                Sweep completed successfully! Swept {executeResult.itemsSwept} item(s), failed {executeResult.itemsFailed} item(s).
              </div>
            </>
          )}
          <div className="execute-result__detail">
            Recovered {formatBytes(executeResult.bytesRecovered)} of disk space
          </div>
        </div>
      )}

      {/* Page Header */}
      <div className="sweep-header">
        <div className="sweep-header__left">
          <h1 className="sweep-title">Sweep Queue</h1>
          <p className="sweep-subtitle">Manage media items flagged for cleanup</p>
        </div>
        <div className="sweep-header__actions">
          <Button
            variant="secondary"
            onClick={handleRunScan}
            loading={scanning}
            iconLeft={<Broom size={16} weight="duotone" />}
          >
            Run Scan
          </Button>
        </div>
      </div>

      {/* Summary Bar */}
      <div className="sweep-summary-bar">
        <div className="sweep-stat">
          <span className="sweep-stat__label">Pending Items</span>
          <span className="sweep-stat__value">{summary?.pendingCount ?? 0}</span>
          <span className="sweep-stat__sub">Flagged and awaiting review</span>
        </div>
        
        <div className="sweep-stat">
          <span className="sweep-stat__label">Reclaimable Space</span>
          <span className="sweep-stat__value sweep-stat__value--accent">
            {formatGb(summary?.pendingBytes ?? 0)}
          </span>
          <span className="sweep-stat__sub">Total size of pending items</span>
        </div>

        <div className="sweep-stat">
          <span className="sweep-stat__label">Approved Items</span>
          <span className="sweep-stat__value sweep-stat__value--warning">
            {summary?.approvedCount ?? 0}
          </span>
          <span className="sweep-stat__sub">Queue items approved for sweep</span>
        </div>

        <div className="sweep-stat">
          <span className="sweep-stat__label">Approved Space</span>
          <span className="sweep-stat__value">
            {formatGb(summary?.approvedBytes ?? 0)}
          </span>
          <span className="sweep-stat__sub">Total size of approved items</span>
        </div>
      </div>

      {/* Toolbar / Filters Row */}
      <div className="sweep-toolbar">
        <div className="filter-tabs">
          {FILTER_STATUSES.map(status => {
            const isActive = statusFilter === status
            const count = status === 'Pending' ? summary?.pendingCount : status === 'Approved' ? summary?.approvedCount : null
            
            return (
              <button
                key={status}
                className={`filter-tab ${isActive ? 'filter-tab--active' : ''}`}
                onClick={() => {
                  setStatusFilter(status)
                  setCurrentPage(1)
                }}
              >
                {status === 'All' ? 'All Items' : status}
                {count !== null && count !== undefined && (
                  <span className="filter-tab__count">{count}</span>
                )}
              </button>
            )
          })}
        </div>

        <div style={{ flex: 1, minWidth: '200px', maxWidth: '300px' }}>
          <Input
            placeholder="Search by title..."
            value={searchQuery}
            onChange={e => setSearchQuery(e.target.value)}
          />
        </div>

        {/* Bulk Action Bar */}
        {selectedIds.size > 0 && (
          <div className="bulk-action-bar">
            <span className="bulk-action-bar__count">{selectedIds.size} item(s) selected</span>
            <Button
              variant="primary"
              size="sm"
              onClick={() => {
                setConfirmTargetIds(Array.from(selectedIds))
                setConfirmAction('bulk-sweep')
                setConfirmOpen(true)
              }}
            >
              Sweep Selected
            </Button>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => {
                setConfirmTargetIds(Array.from(selectedIds))
                setConfirmAction('bulk-ignore')
                setConfirmOpen(true)
              }}
            >
              Ignore Selected
            </Button>
          </div>
        )}
      </div>

      {/* Main Queue View */}
      {loading ? (
        <div className="sweep-loading">
          <Spinner size="lg" />
        </div>
      ) : filteredItems.length === 0 ? (
        <div className="sweep-empty">
          <div className="sweep-empty__icon">
            <Broom size={32} weight="duotone" />
          </div>
          <h2 className="sweep-empty__title">Queue is empty</h2>
          <p className="sweep-empty__body">
            {searchQuery
              ? 'No items match your search query.'
              : 'No media items are currently flagged for sweep. Run a scan to evaluate your rules.'}
          </p>
          {!searchQuery && (
            <Button
              variant="primary"
              onClick={handleRunScan}
              loading={scanning}
              iconLeft={<Play size={16} weight="fill" />}
            >
              Run Scan
            </Button>
          )}
        </div>
      ) : (
        <>
          <div className="sweep-table-wrap">
            <table className="sweep-table">
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input
                      type="checkbox"
                      checked={filteredItems.length > 0 && filteredItems.every(item => selectedIds.has(item.id))}
                      ref={input => {
                        if (input) {
                          const someSelected = filteredItems.some(item => selectedIds.has(item.id))
                          const allSelected = filteredItems.every(item => selectedIds.has(item.id))
                          input.indeterminate = someSelected && !allSelected
                        }
                      }}
                      onChange={e => {
                        const checked = e.target.checked
                        setSelectedIds(prev => {
                          const next = new Set(prev)
                          filteredItems.forEach(item => {
                            if (checked) {
                              next.add(item.id)
                            } else {
                              next.delete(item.id)
                            }
                          })
                          return next
                        })
                      }}
                    />
                  </th>
                  <th className="col-title">Title</th>
                  <th className="col-size">Size</th>
                  <th className="col-why">Why Flagged</th>
                  <th className="col-rule">Rule Group</th>
                  <th className="col-status">Status</th>
                  <th className="col-actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {filteredItems.map(item => {
                  const isSelected = selectedIds.has(item.id)
                  const itemNeedsReview = needsReview(item)
                  const rowClass = [
                    isSelected ? 'sweep-row--selected' : '',
                    item.status === 'Swept' ? 'sweep-row--swept' : '',
                    item.status === 'Failed' ? 'sweep-row--failed' : '',
                  ].filter(Boolean).join(' ')
                  
                  return (
                    <tr key={item.id} className={rowClass}>
                      <td className="col-checkbox">
                        <input
                          type="checkbox"
                          checked={isSelected}
                          disabled={item.status === 'Swept'}
                          onChange={() => {
                            setSelectedIds(prev => {
                              const next = new Set(prev)
                              if (next.has(item.id)) {
                                next.delete(item.id)
                              } else {
                                next.add(item.id)
                              }
                              return next
                            })
                          }}
                        />
                      </td>
                      <td className="col-title">
                        <div className="title-cell">
                          <span className="title-cell__icon" title={item.mediaType}>
                            {MEDIA_ICONS[item.mediaType] || <FilmSlate size={16} weight="duotone" />}
                          </span>
                          <span className="title-cell__text" title={item.title}>
                            {item.title}
                          </span>
                          {item.seasonNumber !== null && item.seasonNumber !== undefined && (
                            <span className="title-cell__season">
                              (S{item.seasonNumber})
                            </span>
                          )}
                          {itemNeedsReview && (
                            <span className="needs-review-badge" title="No matching item found in Radarr/Sonarr">
                              <Warning size={12} weight="fill" /> Needs Review
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="col-size">
                        {formatBytes(item.sizeBytes)}
                      </td>
                      <td className="col-why">
                        <div className="why-cell" title={item.matchedRuleSummary || ''}>
                          {item.matchedRuleSummary || '-'}
                        </div>
                      </td>
                      <td className="col-rule">
                        <div className="rule-cell" title={item.ruleGroupName}>
                          {item.ruleGroupName}
                        </div>
                      </td>
                      <td className="col-status">
                        <Badge variant={STATUS_VARIANTS[item.status]}>
                          {STATUS_LABELS[item.status]}
                        </Badge>
                      </td>
                      <td className="col-actions">
                        <div className="row-actions">
                          {item.status === 'Pending' && (
                            <>
                              <Button
                                variant="primary"
                                size="sm"
                                onClick={() => {
                                  setConfirmTargetIds([item.id])
                                  setConfirmAction('sweep')
                                  setConfirmOpen(true)
                                }}
                              >
                                Sweep
                              </Button>
                              <div className="ignore-popover-wrap">
                                <Button
                                  variant="secondary"
                                  size="sm"
                                  onClick={() => {
                                    setIgnorePopoverItemId(item.id)
                                    setIgnoreCreateExclusion(false)
                                  }}
                                >
                                  Ignore
                                </Button>
                                {ignorePopoverItemId === item.id && (
                                  <div className="ignore-popover">
                                    <label className="ignore-popover__label">
                                      <input
                                        type="checkbox"
                                        checked={ignoreCreateExclusion}
                                        onChange={e => setIgnoreCreateExclusion(e.target.checked)}
                                      />
                                      Exclude permanently?
                                    </label>
                                    <div className="ignore-popover__actions">
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        onClick={() => setIgnorePopoverItemId(null)}
                                      >
                                        Cancel
                                      </Button>
                                      <Button
                                        variant="danger"
                                        size="sm"
                                        onClick={() => handleSingleIgnore(item.id, ignoreCreateExclusion)}
                                      >
                                        Confirm
                                      </Button>
                                    </div>
                                  </div>
                                )}
                              </div>
                            </>
                          )}
                          {item.status === 'Failed' && (
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => {
                                setConfirmTargetIds([item.id])
                                setConfirmAction('sweep')
                                setConfirmOpen(true)
                              }}
                            >
                              Retry
                            </Button>
                          )}
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="pagination-bar">
              <div className="pagination-bar__info">
                Showing {((currentPage - 1) * pageSize) + 1} to {Math.min(currentPage * pageSize, totalItems)} of {totalItems} items
              </div>
              <div className="pagination-bar__controls">
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => setCurrentPage(prev => Math.max(1, prev - 1))}
                  disabled={currentPage === 1 || loading}
                  iconLeft={<ArrowLeft size={14} />}
                >
                  Previous
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => setCurrentPage(prev => Math.min(totalPages, prev + 1))}
                  disabled={currentPage === totalPages || totalPages === 0 || loading}
                  iconRight={<ArrowRight size={14} />}
                >
                  Next
                </Button>
              </div>
            </div>
          )}
        </>
      )}

      {/* Confirmation Modal */}
      <Modal
        open={confirmOpen}
        onClose={() => {
          setConfirmOpen(false)
          setConfirmAction(null)
          setConfirmTargetIds([])
        }}
        title={
          confirmAction === 'bulk-ignore'
            ? 'Ignore Items?'
            : 'Sweep Items?'
        }
        footer={
          <div style={{ display: 'flex', gap: 'var(--space-3)', justifyContent: 'flex-end', width: '100%' }}>
            <Button
              variant="ghost"
              onClick={() => {
                setConfirmOpen(false)
                setConfirmAction(null)
                setConfirmTargetIds([])
              }}
            >
              Cancel
            </Button>
            <Button
              variant={
                confirmAction === 'bulk-ignore'
                  ? 'secondary'
                  : isGlobalDryRun
                  ? 'primary'
                  : 'danger'
              }
              onClick={handleConfirmAction}
              loading={executing}
            >
              {confirmAction === 'bulk-ignore'
                ? `Ignore ${confirmTargetIds.length} Item${confirmTargetIds.length > 1 ? 's' : ''}`
                : isGlobalDryRun
                ? `Simulate Sweep for ${confirmTargetIds.length} Item${confirmTargetIds.length > 1 ? 's' : ''}`
                : `Sweep ${confirmTargetIds.length} Item${confirmTargetIds.length > 1 ? 's' : ''}`}
            </Button>
          </div>
        }
      >
        <div className="confirm-modal__body">
          <div className="confirm-modal__items">
            {confirmTargetIds.slice(0, 5).map(id => {
              const item = items.find(i => i.id === id)
              return (
                <div key={id} className="confirm-modal__item">
                  {item ? item.title : `Item #${id}`}
                </div>
              )
            })}
            {confirmTargetIds.length > 5 && (
              <div className="confirm-modal__more">
                and {confirmTargetIds.length - 5} more...
              </div>
            )}
          </div>

          {confirmAction === 'bulk-ignore' ? (
            <div className="confirm-modal__warning confirm-modal__warning--info">
              <Info className="confirm-modal__warning-icon" size={18} weight="fill" />
              <div>
                Ignoring items removes them from the Sweep Queue.
                <div style={{ marginTop: 'var(--space-2)' }}>
                  <label className="ignore-popover__label" style={{ cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      checked={ignoreCreateExclusion}
                      onChange={e => setIgnoreCreateExclusion(e.target.checked)}
                    />
                    Add to permanent exclusions? (prevents future scans from flagging these items)
                  </label>
                </div>
              </div>
            </div>
          ) : isGlobalDryRun ? (
            <div className="confirm-modal__warning confirm-modal__warning--info">
              <Info className="confirm-modal__warning-icon" size={18} weight="fill" />
              <div>
                <strong>Dry-Run Mode is ON.</strong> Running this sweep will simulate the process in Radarr/Sonarr and Jellyfin. No files will be deleted and no changes will be made to your media servers.
              </div>
            </div>
          ) : (
            <div className="confirm-modal__warning confirm-modal__warning--danger">
              <Warning className="confirm-modal__warning-icon" size={18} weight="fill" />
              <div>
                <strong>Warning: This action is permanent!</strong> This will delete the files from your disk and unmonitor the selected items in Radarr/Sonarr. <strong>This cannot be undone.</strong>
              </div>
            </div>
          )}

          {(confirmAction === 'sweep' || confirmAction === 'bulk-sweep') && 
            confirmTargetIds.some(id => {
              const item = items.find(i => i.id === id)
              return item && needsReview(item)
            }) && (
              <div className="confirm-modal__needs-review-note">
                <Warning size={14} weight="fill" style={{ marginRight: '4px', verticalAlign: 'middle' }} />
                Warning: One or more selected items could not be confirmed in Radarr/Sonarr (unmatched or ambiguous provider IDs). Sweeping these items might not unmonitor them correctly. Proceed anyway?
              </div>
            )}
        </div>
      </Modal>
    </div>
  )
}
