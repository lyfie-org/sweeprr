import { useCallback, useEffect, useState } from 'react'
import {
  FilmSlate,
  Television,
  VideoCamera,
  ArrowLeft,
  ArrowRight,
  ArrowsDownUp,
  LockSimple,
  Compass,
} from '@phosphor-icons/react'
import { mediaApi, type MediaItem, type MediaQueryParams } from '../api/media'
import { rulesApi, type RuleGroupResponse, type MediaType, MEDIA_TYPE_LABELS } from '../api/rules'
import { STATUS_LABELS, STATUS_VARIANTS, type SweepItemStatus } from '../api/sweep'
import { Badge, Button, Input, Spinner, useToast } from '../components/ui'
import { RuleTraceDrawer } from '../components/media/RuleTraceDrawer'
import { BulkActionBar } from '../components/media/BulkActionBar'
import { ExcludeScopeModal } from '../components/media/ExcludeScopeModal'
import './MediaPage.css'

const MEDIA_ICONS: Record<string, React.ReactNode> = {
  Movie:   <FilmSlate size={16} weight="duotone" />,
  Series:  <Television size={16} weight="duotone" />,
  Season:  <Television size={16} weight="duotone" />,
  Episode: <VideoCamera size={16} weight="duotone" />,
}

const TYPE_OPTIONS: (MediaType | 'All')[] = ['All', 'Movie', 'Series', 'Season', 'Episode']
const STATUS_OPTIONS: (SweepItemStatus | 'All')[] = ['All', 'Pending', 'Approved', 'Ignored', 'Swept', 'Failed']

type SortBy = NonNullable<MediaQueryParams['sortBy']>

const SORT_OPTIONS: { value: SortBy; label: string }[] = [
  { value: 'title',       label: 'Title' },
  { value: 'year',        label: 'Year' },
  { value: 'sizegb',      label: 'Size' },
  { value: 'lastwatched', label: 'Last Watched' },
  { value: 'status',      label: 'Status' },
]

function formatDate(iso: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}

export function MediaPage() {
  const { toast } = useToast()

  const [items, setItems] = useState<MediaItem[]>([])
  const [totalItems, setTotalItems] = useState(0)
  const [currentPage, setCurrentPage] = useState(1)
  const [loading, setLoading] = useState(false)

  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [typeFilter, setTypeFilter] = useState<MediaType | 'All'>('All')
  const [statusFilter, setStatusFilter] = useState<SweepItemStatus | 'All'>('All')
  const [sortBy, setSortBy] = useState<SortBy>('title')
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc')

  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
  const [ruleGroups, setRuleGroups] = useState<RuleGroupResponse[]>([])

  const [traceItemId, setTraceItemId] = useState<string | null>(null)
  const [excludeModalOpen, setExcludeModalOpen] = useState(false)
  const [bulkBusy, setBulkBusy] = useState(false)

  const pageSize = 25

  // ── Fetching ────────────────────────────────────────────────────────────────

  const fetchItems = useCallback(async () => {
    setLoading(true)
    try {
      const data = await mediaApi.getAll({
        search: search.trim() || undefined,
        type: typeFilter === 'All' ? undefined : typeFilter,
        status: statusFilter === 'All' ? undefined : statusFilter,
        sortBy,
        sortDir,
        page: currentPage,
        pageSize,
      })
      setItems(data.items)
      setTotalItems(data.totalCount)
    } catch (err: any) {
      toast({
        type: 'error',
        title: 'Error Fetching Media',
        message: err.message || 'An error occurred while loading the media library.',
      })
    } finally {
      setLoading(false)
    }
  }, [search, typeFilter, statusFilter, sortBy, sortDir, currentPage, toast])

  useEffect(() => { fetchItems() }, [fetchItems])

  useEffect(() => {
    rulesApi.getAll().then(setRuleGroups).catch(() => {})
  }, [])

  // Debounce free-text search
  useEffect(() => {
    const handle = setTimeout(() => {
      setSearch(searchInput)
      setCurrentPage(1)
    }, 300)
    return () => clearTimeout(handle)
  }, [searchInput])

  // Reset selection when the visible page changes
  useEffect(() => {
    setSelectedIds(new Set())
  }, [items])

  // ── Action Handlers ────────────────────────────────────────────────────────

  const handleScheduleLeavingSoon = async () => {
    setBulkBusy(true)
    try {
      const ids = Array.from(selectedIds)
      const result = await mediaApi.queueManual(ids)
      toast({
        type: 'success',
        title: 'Added to Sweep Queue',
        message: result.alreadyQueued > 0
          ? `Queued ${result.queued} item(s); ${result.alreadyQueued} were already queued.`
          : `Queued ${result.queued} item(s).`,
      })
      setSelectedIds(new Set())
      fetchItems()
    } catch (err: any) {
      toast({
        type: 'error',
        title: 'Failed to Queue Items',
        message: err.message || 'An error occurred while updating the sweep queue.',
      })
    } finally {
      setBulkBusy(false)
    }
  }

  const handleExcludeConfirm = async (ruleGroupId: number | null) => {
    setBulkBusy(true)
    try {
      const ids = Array.from(selectedIds)
      const result = await mediaApi.excludeBulk({ ids, ruleGroupId })
      toast({
        type: 'success',
        title: 'Exclusions Added',
        message: `Excluded ${result.excluded} item(s) from future scans.`,
      })
      setSelectedIds(new Set())
      setExcludeModalOpen(false)
      fetchItems()
    } catch (err: any) {
      toast({
        type: 'error',
        title: 'Failed to Add Exclusions',
        message: err.message || 'An error occurred while adding exclusions.',
      })
    } finally {
      setBulkBusy(false)
    }
  }

  const toggleSelect = (id: string) => {
    setSelectedIds(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  // ── Render ──────────────────────────────────────────────────────────────────

  const totalPages = Math.ceil(totalItems / pageSize)
  const allSelected = items.length > 0 && items.every(item => selectedIds.has(item.id))
  const someSelected = items.some(item => selectedIds.has(item.id))

  return (
    <div className="media-page">
      {/* Page Header */}
      <div className="media-header">
        <div className="media-header__left">
          <h1 className="media-title">Media Explorer</h1>
          <p className="media-subtitle">Browse, curate, and review every item the rule engine has touched</p>
        </div>
      </div>

      {/* Toolbar / Filters Row */}
      <div className="media-toolbar">
        <div className="media-search">
          <Input
            placeholder="Search by title..."
            value={searchInput}
            onChange={e => setSearchInput(e.target.value)}
          />
        </div>

        <div className="media-filter">
          <span className="media-filter__label">Type</span>
          <select
            className="media-select"
            value={typeFilter}
            onChange={e => { setTypeFilter(e.target.value as MediaType | 'All'); setCurrentPage(1) }}
          >
            {TYPE_OPTIONS.map(opt => (
              <option key={opt} value={opt}>{opt === 'All' ? 'All Types' : MEDIA_TYPE_LABELS[opt]}</option>
            ))}
          </select>
        </div>

        <div className="media-filter">
          <span className="media-filter__label">Status</span>
          <select
            className="media-select"
            value={statusFilter}
            onChange={e => { setStatusFilter(e.target.value as SweepItemStatus | 'All'); setCurrentPage(1) }}
          >
            {STATUS_OPTIONS.map(opt => (
              <option key={opt} value={opt}>{opt === 'All' ? 'All Statuses' : STATUS_LABELS[opt]}</option>
            ))}
          </select>
        </div>

        <div className="media-filter">
          <span className="media-filter__label">Sort</span>
          <select
            className="media-select"
            value={sortBy}
            onChange={e => { setSortBy(e.target.value as SortBy); setCurrentPage(1) }}
          >
            {SORT_OPTIONS.map(opt => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
          <button
            className="media-sort-dir"
            onClick={() => { setSortDir(d => d === 'asc' ? 'desc' : 'asc'); setCurrentPage(1) }}
            title={sortDir === 'asc' ? 'Ascending' : 'Descending'}
            aria-label="Toggle sort direction"
          >
            <ArrowsDownUp size={14} weight="bold" />
          </button>
        </div>
      </div>

      {/* Bulk Action Bar */}
      {selectedIds.size > 0 && (
        <BulkActionBar
          count={selectedIds.size}
          busy={bulkBusy}
          onScheduleLeavingSoon={handleScheduleLeavingSoon}
          onAddExclusion={() => setExcludeModalOpen(true)}
          onClear={() => setSelectedIds(new Set())}
        />
      )}

      {/* Main Table */}
      {loading ? (
        <div className="media-loading">
          <Spinner size="lg" />
        </div>
      ) : items.length === 0 ? (
        <div className="media-empty">
          <div className="media-empty__icon">
            <Compass size={32} weight="duotone" />
          </div>
          <h2 className="media-empty__title">No media found</h2>
          <p className="media-empty__body">
            {search || typeFilter !== 'All' || statusFilter !== 'All'
              ? 'No items match your current filters.'
              : 'No media items have been evaluated yet. Run a scan from the Sweep Queue to populate this view.'}
          </p>
        </div>
      ) : (
        <>
          <div className="media-table-wrap">
            <table className="media-table">
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input
                      type="checkbox"
                      checked={allSelected}
                      ref={input => {
                        if (input) input.indeterminate = someSelected && !allSelected
                      }}
                      onChange={e => {
                        const checked = e.target.checked
                        setSelectedIds(prev => {
                          const next = new Set(prev)
                          items.forEach(item => {
                            if (checked) next.add(item.id)
                            else next.delete(item.id)
                          })
                          return next
                        })
                      }}
                    />
                  </th>
                  <th className="col-title">Title</th>
                  <th className="col-size">Size</th>
                  <th className="col-watched">Last Watched</th>
                  <th className="col-status">Status</th>
                  <th className="col-rules">Rules</th>
                </tr>
              </thead>
              <tbody>
                {items.map(item => {
                  const isSelected = selectedIds.has(item.id)
                  const matchCount = item.matchedRuleGroups.length

                  return (
                    <tr key={item.id} className={isSelected ? 'media-row--selected' : ''}>
                      <td className="col-checkbox">
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={() => toggleSelect(item.id)}
                        />
                      </td>
                      <td className="col-title">
                        <div className="title-cell">
                          <span className="title-cell__icon" title={item.type}>
                            {MEDIA_ICONS[item.type] || <FilmSlate size={16} weight="duotone" />}
                          </span>
                          <span className="title-cell__text" title={item.title}>
                            {item.title}
                          </span>
                          {item.year != null && (
                            <span className="title-cell__year">({item.year})</span>
                          )}
                          {item.isExcluded && (
                            <span className="excluded-badge" title="Excluded from future rule matching">
                              <LockSimple size={12} weight="fill" /> Excluded
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="col-size">{item.sizeLabel}</td>
                      <td className="col-watched">
                        {item.lastWatched ? (
                          <span title={`Watched by ${item.watchedByCount} user(s)`}>
                            {formatDate(item.lastWatched)}
                          </span>
                        ) : (
                          <span className="text-muted">—</span>
                        )}
                      </td>
                      <td className="col-status">
                        {item.status ? (
                          <Badge variant={STATUS_VARIANTS[item.status]}>
                            {STATUS_LABELS[item.status]}
                          </Badge>
                        ) : (
                          <span className="text-muted">—</span>
                        )}
                      </td>
                      <td className="col-rules">
                        <button
                          className="rules-cell"
                          onClick={() => setTraceItemId(item.id)}
                          title="View rule trace"
                        >
                          {matchCount > 0 ? (
                            <Badge variant={item.status === 'Pending' ? 'danger' : 'warning'} dot>
                              {matchCount}
                            </Badge>
                          ) : (
                            <span className="text-muted">—</span>
                          )}
                        </button>
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

      <RuleTraceDrawer itemId={traceItemId} onClose={() => setTraceItemId(null)} />

      <ExcludeScopeModal
        open={excludeModalOpen}
        onClose={() => setExcludeModalOpen(false)}
        onConfirm={handleExcludeConfirm}
        ruleGroups={ruleGroups}
        count={selectedIds.size}
        busy={bulkBusy}
      />
    </div>
  )
}
