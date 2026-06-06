import { api } from './client'

export const CONNECTION_TYPE_LABELS = {
  0: 'Jellyfin',
  1: 'Radarr',
  2: 'Sonarr',
} as const

export type ConnectionType = 0 | 1 | 2

export interface ConnectionResponse {
  id: number
  name: string
  type: ConnectionType
  baseUrl: string
  maskedKey: string
  hasKey: boolean
  isEnabled: boolean
  allowInsecure: boolean
  lastConnectedAt: string | null
  lastConnectionOk: boolean | null
}

export interface ConnectionRequest {
  name: string
  type: ConnectionType
  baseUrl: string
  apiKey?: string | null
  isEnabled: boolean
  allowInsecure: boolean
}

export interface CreateConnectionResponse {
  connection: ConnectionResponse
  warning: string | null
}

export interface ConnectionTestResult {
  success: boolean
  serverName: string | null
  version: string | null
  latencyMs: number | null
  errorMessage: string | null
}

export const connectionsApi = {
  getAll: () =>
    api.get<ConnectionResponse[]>('/api/connections'),

  create: (req: ConnectionRequest) =>
    api.post<CreateConnectionResponse>('/api/connections', req),

  update: (id: number, req: ConnectionRequest) =>
    api.put<ConnectionResponse>(`/api/connections/${id}`, req),

  delete: (id: number) =>
    api.delete<void>(`/api/connections/${id}`),

  testSaved: (id: number) =>
    api.post<ConnectionTestResult>(`/api/connections/${id}/test`),

  testUnsaved: (
    type: ConnectionType,
    baseUrl: string,
    apiKey: string,
    allowInsecure: boolean,
  ) =>
    api.post<ConnectionTestResult>('/api/connections/test-unsaved', {
      type,
      baseUrl,
      apiKey,
      allowInsecure,
    }),
}
