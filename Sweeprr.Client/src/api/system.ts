import { api } from './client'

export interface SystemInfoDto {
  version: string
  releaseDate: string
}

export const systemApi = {
  getInfo: () => api.get<SystemInfoDto>('/api/system/info'),
}
