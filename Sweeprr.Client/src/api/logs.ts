import { api } from './client'

export interface LogEntry {
  timestamp: string
  level: string
  category: string
  sourceContext: string
  message: string
  exception: string | null
}

export interface LogResponse {
  items: LogEntry[]
  totalCount: number
  page: number
  pageSize: number
}

export interface LogFilters {
  level?: string
  category?: string
  search?: string
  page?: number
  pageSize?: number
}

export const logsApi = {
  getLogs: (filters: LogFilters) => {
    const params = new URLSearchParams()
    if (filters.level) params.append('level', filters.level)
    if (filters.category) params.append('category', filters.category)
    if (filters.search) params.append('search', filters.search)
    if (filters.page) params.append('page', filters.page.toString())
    if (filters.pageSize) params.append('pageSize', filters.pageSize.toString())

    const query = params.toString()
    return api.get<LogResponse>(`/api/logs${query ? `?${query}` : ''}`)
  },
}
