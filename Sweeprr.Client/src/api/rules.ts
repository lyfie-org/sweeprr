import { api } from './client'

// ── Types mirroring backend DTOs ─────────────────────────────────────────────

export type MediaType = 'Movie' | 'Series' | 'Season' | 'Episode'
export type SweepAction =
  | 'DeleteAndUnmonitor'
  | 'UnmonitorOnly'
  | 'DeleteOnly'
  | 'DeleteSeriesIfEmpty'
  | 'UnmonitorSeasonIfEmpty'
  | 'ChangeQualityProfile'
export type LogicalOperator = 'And' | 'Or'
export type RuleValueType = 'Number' | 'Date' | 'Text' | 'Bool' | 'RelativeDays' | 'TextList'
export type RuleComparator =
  | 'Equals'
  | 'NotEquals'
  | 'GreaterThan'
  | 'LessThan'
  | 'Contains'
  | 'NotContains'
  | 'Before'
  | 'After'
  | 'InLastDays'
  | 'NotInLastDays'
  | 'Exists'
  | 'NotExists'

export interface RuleConditionDto {
  section: number
  logicalOperator: LogicalOperator | null
  field: string
  comparator: RuleComparator
  value: string
  valueType: RuleValueType
}

export interface RuleConditionResponse extends RuleConditionDto {
  id: number
}

export interface RuleGroupResponse {
  id: number
  name: string
  description: string | null
  mediaType: MediaType
  isEnabled: boolean
  cronOverride: string | null
  action: SweepAction
  targetQualityProfileId: number | null
  targetQualityProfileName: string | null
  createdAt: string
  updatedAt: string
  conditions: RuleConditionResponse[]
}

export interface RuleGroupRequest {
  name: string
  description?: string | null
  mediaType: MediaType
  isEnabled: boolean
  cronOverride?: string | null
  action: SweepAction
  targetQualityProfileId?: number | null
  targetQualityProfileName?: string | null
  conditions: RuleConditionDto[]
}

export interface QualityProfileDto {
  id: number
  name: string
}

export interface FieldDescriptor {
  field: string
  label: string
  primaryValueType: RuleValueType
  applicableMediaTypes: MediaType[]
  allowedComparators: RuleComparator[]
}

export interface FieldsMetaResponse {
  fields: FieldDescriptor[]
}

export interface TagDto {
  id: number
  label: string
}

export interface PreviewRequest {
  mediaType: MediaType
  conditions: RuleConditionDto[]
}

export interface PreviewResponse {
  matchCount: number
  sampleTitles: string[]
  note: string | null
}

export interface SimulateRequest {
  mediaType: MediaType
  conditions: RuleConditionDto[]
}

export interface SimulateLibraryBreakdown {
  library: string
  matchedCount: number
  reclaimedGb: number
}

export interface SimulateResponse {
  matchedCount: number
  totalReclaimedGb: number
  categoryBreakdown: Record<string, number>
  libraryBreakdown: SimulateLibraryBreakdown[]
  sampleTitles: string[]
  note: string | null
}

// ── API module ────────────────────────────────────────────────────────────────

export const rulesApi = {
  getAll: () =>
    api.get<RuleGroupResponse[]>('/api/rulegroups'),

  getById: (id: number) =>
    api.get<RuleGroupResponse>(`/api/rulegroups/${id}`),

  getFieldsMeta: () =>
    api.get<FieldsMetaResponse>('/api/rulegroups/fields'),

  getTags: (connectionId: number) =>
    api.get<{ tags: TagDto[] }>(`/api/rulegroups/tags?connectionId=${connectionId}`),

  getGenres: () =>
    api.get<{ genres: string[] }>('/api/rulegroups/genres'),

  create: (req: RuleGroupRequest) =>
    api.post<RuleGroupResponse>('/api/rulegroups', req),

  update: (id: number, req: RuleGroupRequest) =>
    api.put<RuleGroupResponse>(`/api/rulegroups/${id}`, req),

  delete: (id: number) =>
    api.delete<void>(`/api/rulegroups/${id}`),

  preview: (req: PreviewRequest) =>
    api.post<PreviewResponse>('/api/rulegroups/preview', req),

  simulate: (req: SimulateRequest) =>
    api.post<SimulateResponse>('/api/rulegroups/simulate', req),

  scan: (id: number) =>
    api.post<{ ruleGroupId: number; ruleGroupName: string; itemsFlagged: number; durationMs: number }>(
      `/api/rulegroups/${id}/scan`,
    ),
}

// ── Helpers ───────────────────────────────────────────────────────────────────

export const MEDIA_TYPE_LABELS: Record<MediaType, string> = {
  Movie: 'Movie',
  Series: 'Series',
  Season: 'Season',
  Episode: 'Episode',
}

export const ACTION_LABELS: Record<SweepAction, string> = {
  DeleteAndUnmonitor: 'Delete & Unmonitor',
  UnmonitorOnly: 'Unmonitor Only',
  DeleteOnly: 'Delete Only',
  DeleteSeriesIfEmpty: 'Delete Series If Empty',
  UnmonitorSeasonIfEmpty: 'Unmonitor Season If Empty',
  ChangeQualityProfile: 'Change Quality Profile',
}

export const COMPARATOR_LABELS: Record<RuleComparator, string> = {
  Equals: 'equals',
  NotEquals: 'not equals',
  GreaterThan: 'greater than',
  LessThan: 'less than',
  Contains: 'contains',
  NotContains: 'not contains',
  Before: 'before',
  After: 'after',
  InLastDays: 'in last N days',
  NotInLastDays: 'not in last N days',
  Exists: 'exists',
  NotExists: 'not exists',
}

/** Comparators that take no user-supplied value */
export const VALUELESS_COMPARATORS = new Set<RuleComparator>(['Exists', 'NotExists'])

/** For a given comparator, the valueType it requires (null = use field's primaryValueType) */
export function inferValueType(
  comparator: RuleComparator,
  fieldValueType: RuleValueType,
): RuleValueType {
  if (comparator === 'InLastDays' || comparator === 'NotInLastDays') return 'RelativeDays'
  if (comparator === 'Before' || comparator === 'After') return 'Date'
  if (comparator === 'GreaterThan' || comparator === 'LessThan') return 'Number'
  return fieldValueType
}
