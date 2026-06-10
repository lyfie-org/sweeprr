import { api } from './client'
import type { LogicalOperator, MediaType, RuleComparator } from './rules'
import type { SweepItemStatus } from './sweep'

// ── Types mirroring backend DTOs ─────────────────────────────────────────────

export interface MatchedRuleGroupDto {
  ruleGroupId: number
  ruleGroupName: string
  matchReason: string
}

export interface MediaItem {
  id: string
  title: string
  year: number | null
  type: MediaType
  sizeGb: number
  sizeLabel: string
  lastWatched: string | null
  watchedByCount: number
  status: SweepItemStatus | null
  matchedRuleGroups: MatchedRuleGroupDto[]
  tags: string[]
  isExcluded: boolean
}

export interface PagedMediaResponse {
  items: MediaItem[]
  totalCount: number
  page: number
  pageSize: number
}

export interface MediaQueryParams {
  search?: string
  type?: MediaType
  status?: SweepItemStatus
  sortBy?: 'title' | 'year' | 'sizegb' | 'lastwatched' | 'status'
  sortDir?: 'asc' | 'desc'
  page?: number
  pageSize?: number
}

export interface ClauseTraceResult {
  section: number
  logicalOperator: LogicalOperator | null
  field: string
  comparator: RuleComparator
  value: string
  result: boolean | null
}

export interface RuleTraceEvaluation {
  ruleGroupId: number
  ruleGroupName: string
  matched: boolean
  clauseResults: ClauseTraceResult[]
}

export interface RuleTraceResponse {
  itemId: string
  title: string
  evaluations: RuleTraceEvaluation[]
}

export interface QueueManualResponse {
  queued: number
  alreadyQueued: number
}

export interface ExcludeBulkRequest {
  ids: string[]
  ruleGroupId?: number | null
  reason?: string | null
  expiresAt?: string | null
}

export interface ExcludeBulkResponse {
  excluded: number
}

// ── API module ────────────────────────────────────────────────────────────────

export const mediaApi = {
  getAll: (params: MediaQueryParams = {}) => {
    const qs = new URLSearchParams()
    if (params.search) qs.set('search', params.search)
    if (params.type) qs.set('type', params.type)
    if (params.status) qs.set('status', params.status)
    if (params.sortBy) qs.set('sortBy', params.sortBy)
    if (params.sortDir) qs.set('sortDir', params.sortDir)
    if (params.page != null) qs.set('page', String(params.page))
    if (params.pageSize != null) qs.set('pageSize', String(params.pageSize))
    const query = qs.toString()
    return api.get<PagedMediaResponse>(`/api/media${query ? `?${query}` : ''}`)
  },

  getRuleTrace: (id: string) =>
    api.get<RuleTraceResponse>(`/api/media/${encodeURIComponent(id)}/ruletrace`),

  queueManual: (ids: string[]) =>
    api.post<QueueManualResponse>('/api/media/queue-manual', { ids }),

  excludeBulk: (req: ExcludeBulkRequest) =>
    api.post<ExcludeBulkResponse>('/api/media/exclude-bulk', req),
}
