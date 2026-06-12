import { api } from './client'

export interface ExclusionResponse {
  id: number
  mediaServerItemId: string
  reason: string | null
  createdAt: string
  ruleGroupId: number | null
  ruleGroupName: string | null
  expiresAt: string | null
  createdBy: string | null
}

export interface TagExclusionRequest {
  tagName: string
  tagId: number
  serverConnectionId: number
  ruleGroupId: number | null
}

export interface TagExclusionResponse {
  id: number
  tagName: string
  tagId: number
  serverConnectionId: number
  connectionName: string
  ruleGroupId: number | null
  ruleGroupName: string | null
}

export const exclusionsApi = {
  // Media exclusions
  getAll: () =>
    api.get<ExclusionResponse[]>('/api/exclusions'),

  delete: (id: number) =>
    api.delete<void>(`/api/exclusions/${id}`),

  // Tag exclusions
  getTags: () =>
    api.get<TagExclusionResponse[]>('/api/exclusions/tags'),

  createTag: (req: TagExclusionRequest) =>
    api.post<TagExclusionResponse>('/api/exclusions/tags', req),

  deleteTag: (id: number) =>
    api.delete<void>(`/api/exclusions/tags/${id}`),
}
