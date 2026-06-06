import { api } from './client'

export interface SettingsDto {
  instanceName: string
  globalDryRun: boolean
  defaultCron: string
  maxItemsPerRun: number
  maxGbPerRun: number
  pessimisticSizeGb: number
  libraryPercentCap: number | null
  overBroadMatchPct: number | null
}

export interface UpdateSettingsRequest {
  instanceName?: string
  globalDryRun?: boolean
  defaultCron?: string
  maxItemsPerRun?: number
  maxGbPerRun?: number
  pessimisticSizeGb?: number
  libraryPercentCap?: number
  clearLibraryPercentCap?: boolean
  overBroadMatchPct?: number
  clearOverBroadMatchPct?: boolean
}

export const settingsApi = {
  get: () =>
    api.get<SettingsDto>('/api/settings'),

  patch: (req: UpdateSettingsRequest) =>
    api.patch<SettingsDto>('/api/settings', req),
}
