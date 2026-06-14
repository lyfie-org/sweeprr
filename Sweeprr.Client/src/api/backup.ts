import { api } from './client'

export const BACKUP_DESTINATION_TYPES = ['Local', 'S3'] as const

export type BackupDestinationType = (typeof BACKUP_DESTINATION_TYPES)[number]

export interface BackupSettingResponse {
  isEnabled: boolean
  destinationType: BackupDestinationType
  localPath: string | null
  s3Endpoint: string | null
  s3Region: string | null
  s3Bucket: string | null
  s3AccessKey: string | null
  maskedS3SecretKey: string | null
  retentionCount: number
  scheduleCron: string
  nextScheduledRun: string | null
}

export interface UpdateBackupSettingRequest {
  isEnabled?: boolean
  destinationType?: BackupDestinationType
  localPath?: string
  s3Endpoint?: string
  s3Region?: string
  s3Bucket?: string
  s3AccessKey?: string
  s3SecretKey?: string
  retentionCount?: number
  scheduleCron?: string
}

export interface TriggerBackupResponse {
  success: boolean
  filename: string | null
  sizeKb: number | null
  error: string | null
}

export interface BackupHistoryEntry {
  filename: string
  sizeBytes: number
  createdAt: string
}

export const backupApi = {
  get: () => api.get<BackupSettingResponse>('/api/settings/backup'),

  update: (req: UpdateBackupSettingRequest) =>
    api.put<BackupSettingResponse>('/api/settings/backup', req),

  trigger: () => api.post<TriggerBackupResponse>('/api/settings/backup/trigger'),

  getHistory: () => api.get<BackupHistoryEntry[]>('/api/settings/backup/history'),
}
