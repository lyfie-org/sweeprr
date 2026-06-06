import { api } from './client'

export interface DashboardStats {
  totalGbRecovered: number
  totalItemsSwept: number
  itemsSweptLast30d: number
  gbRecoveredLast30d: number
  pendingQueueCount: number
  nextScheduledRun: string | null
  wsState: 'Connected' | 'Connecting' | 'Reconnecting' | 'Disconnected'
  globalDryRun: boolean
}

export interface ActivityLogEntry {
  id: number
  timestamp: string
  level: 'Debug' | 'Information' | 'Warning' | 'Error'
  category: 'Sweep' | 'Connection' | 'Rule' | 'System' | 'Auth'
  message: string
  metaJson: string | null
}

export interface SparklinePoint {
  date: string
  gbRecovered: number
  itemsSwept: number
}

export const dashboardApi = {
  getStats: () =>
    api.get<DashboardStats>('/api/dashboard/stats'),

  getActivity: (limit = 20) =>
    api.get<ActivityLogEntry[]>(`/api/dashboard/activity?limit=${limit}`),

  getSparkline: (days = 30) =>
    api.get<SparklinePoint[]>(`/api/dashboard/sparkline?days=${days}`),
}
