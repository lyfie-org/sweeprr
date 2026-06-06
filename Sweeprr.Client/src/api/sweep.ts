import { api } from './client'

// ── Types mirroring backend DTOs ─────────────────────────────────────────────

export type SweepItemStatus = 'Pending' | 'Approved' | 'Ignored' | 'Swept' | 'Failed'
export type SweepMediaType = 'Movie' | 'Series' | 'Season' | 'Episode'

export interface SweepItem {
  id: number
  ruleGroupId: number
  ruleGroupName: string
  mediaServerItemId: string
  title: string
  mediaType: SweepMediaType
  sizeBytes: number | null
  matchedRuleSummary: string | null
  status: SweepItemStatus
  arrInstanceId: number | null
  tmdbId: string | null
  tvdbId: string | null
  imdbId: string | null
  flaggedAt: string
  sweptAt: string | null
  skippedReason: string | null
  seasonNumber: number | null
}

export interface SweepSummary {
  pendingCount: number
  approvedCount: number
  pendingBytes: number
  approvedBytes: number
}

export interface PagedSweepResponse {
  items: SweepItem[]
  total: number
  page: number
  pageSize: number
}

export interface ExecuteSweepRequest {
  itemIds?: number[]
}

export interface ExecuteSweepResult {
  itemsSwept: number
  itemsFailed: number
  itemsSkippedByFailsafe: number
  bytesRecovered: number
  wasDryRun: boolean
}

export interface RunSweepGroupResult {
  ruleGroupId: number
  ruleGroupName?: string
  itemsFlagged?: number
  durationMs?: number
  error?: string
}

export interface RunSweepResult {
  isDryRun: boolean
  results: RunSweepGroupResult[]
}

export interface SweepQueryParams {
  status?: SweepItemStatus | 'All'
  ruleGroupId?: number
  page?: number
  pageSize?: number
}

export interface IgnoreRequest {
  createExclusion?: boolean
}

export interface SkipRequest {
  reason?: string
}

// ── API module ────────────────────────────────────────────────────────────────

export const sweepApi = {
  getAll: (params: SweepQueryParams = {}) => {
    const qs = new URLSearchParams()
    if (params.status && params.status !== 'All') qs.set('status', params.status)
    if (params.ruleGroupId != null) qs.set('ruleGroupId', String(params.ruleGroupId))
    if (params.page != null) qs.set('page', String(params.page))
    if (params.pageSize != null) qs.set('pageSize', String(params.pageSize))
    const query = qs.toString()
    return api.get<PagedSweepResponse>(`/api/sweep${query ? `?${query}` : ''}`)
  },

  getSummary: () =>
    api.get<SweepSummary>('/api/sweep/summary'),

  getById: (id: number) =>
    api.get<SweepItem>(`/api/sweep/${id}`),

  approve: (id: number) =>
    api.post<SweepItem>(`/api/sweep/${id}/approve`),

  ignore: (id: number, createExclusion = false) =>
    api.post<SweepItem>(`/api/sweep/${id}/ignore`, { createExclusion } satisfies IgnoreRequest),

  skip: (id: number, reason?: string) =>
    api.post<SweepItem>(`/api/sweep/${id}/skip`, { reason } satisfies SkipRequest),

  execute: (req: ExecuteSweepRequest = {}) =>
    api.post<ExecuteSweepResult>('/api/sweep/execute', req),

  run: (ruleGroupId?: number) =>
    api.post<RunSweepResult>('/api/sweep/run', ruleGroupId != null ? { ruleGroupId } : {}),
}

// ── Helpers ───────────────────────────────────────────────────────────────────

/** True if the item has no provider IDs and no arr instance — needs review */
export function needsReview(item: SweepItem): boolean {
  return (
    item.arrInstanceId == null &&
    !item.tmdbId &&
    !item.tvdbId &&
    !item.imdbId
  )
}

/** Format bytes to human-readable string */
export function formatBytes(bytes: number | null | undefined, decimals = 1): string {
  if (bytes == null) return '?'
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(decimals))} ${sizes[i]}`
}

/** Format bytes specifically as GB (for the summary bar) */
export function formatGb(bytes: number): string {
  const gb = bytes / 1_073_741_824
  if (gb < 0.1) return '< 0.1 GB'
  if (gb < 10) return `${gb.toFixed(2)} GB`
  return `${gb.toFixed(1)} GB`
}

export const STATUS_LABELS: Record<SweepItemStatus, string> = {
  Pending:  'Pending',
  Approved: 'Approved',
  Ignored:  'Ignored',
  Swept:    'Swept',
  Failed:   'Failed',
}

export const STATUS_VARIANTS: Record<SweepItemStatus, 'warning' | 'info' | 'neutral' | 'success' | 'danger'> = {
  Pending:  'warning',
  Approved: 'info',
  Ignored:  'neutral',
  Swept:    'success',
  Failed:   'danger',
}
