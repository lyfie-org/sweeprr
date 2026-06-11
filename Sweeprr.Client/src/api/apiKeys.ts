import { api } from './client'

export const API_KEY_SCOPES = ['read:sweep', 'write:sweep', 'execute:sweep', 'admin'] as const

export type ApiKeyScope = (typeof API_KEY_SCOPES)[number]

export interface ApiKeyResponse {
  id: number
  name: string
  maskedKey: string
  createdBy: string
  createdAt: string
  expiresAt: string | null
  lastUsedAt: string | null
  scopes: ApiKeyScope[]
  isActive: boolean
}

export interface GenerateApiKeyRequest {
  name: string
  scopes: ApiKeyScope[]
  expiresAt?: string | null
}

export interface GenerateApiKeyResponse {
  id: number
  name: string
  rawKey: string
  maskedKey: string
  scopes: ApiKeyScope[]
  expiresAt: string | null
  warning: string
}

export const apiKeysApi = {
  getAll: () =>
    api.get<ApiKeyResponse[]>('/api/settings/keys'),

  generate: (req: GenerateApiKeyRequest) =>
    api.post<GenerateApiKeyResponse>('/api/settings/keys', req),

  revoke: (id: number) =>
    api.delete<void>(`/api/settings/keys/${id}`),
}
