import { api } from './client'

export const NOTIFICATION_PROVIDER_TYPES = ['Discord', 'GenericWebhook'] as const

export type NotificationProviderType = (typeof NOTIFICATION_PROVIDER_TYPES)[number]

export interface NotificationSettingResponse {
  id: number
  name: string
  providerType: NotificationProviderType
  maskedWebhookUrl: string
  isEnabled: boolean
  triggerOnFailsafe: boolean
  triggerOnSweepComplete: boolean
  triggerOnPendingItems: boolean
  triggerOnConnectionError: boolean
  createdAt: string
}

export interface CreateNotificationSettingRequest {
  name: string
  providerType: NotificationProviderType
  webhookUrl: string
  isEnabled: boolean
  triggerOnFailsafe: boolean
  triggerOnSweepComplete: boolean
  triggerOnPendingItems: boolean
  triggerOnConnectionError: boolean
}

export interface UpdateNotificationSettingRequest {
  name?: string
  webhookUrl?: string
  isEnabled?: boolean
  triggerOnFailsafe?: boolean
  triggerOnSweepComplete?: boolean
  triggerOnPendingItems?: boolean
  triggerOnConnectionError?: boolean
}

export interface TestNotificationRequest {
  providerType: NotificationProviderType
  webhookUrl: string
}

export interface TestNotificationResponse {
  success: boolean
  error: string | null
}

export const notificationsApi = {
  getAll: () =>
    api.get<NotificationSettingResponse[]>('/api/settings/notifications'),

  create: (req: CreateNotificationSettingRequest) =>
    api.post<NotificationSettingResponse>('/api/settings/notifications', req),

  update: (id: number, req: UpdateNotificationSettingRequest) =>
    api.put<NotificationSettingResponse>(`/api/settings/notifications/${id}`, req),

  delete: (id: number) =>
    api.delete<void>(`/api/settings/notifications/${id}`),

  test: (req: TestNotificationRequest) =>
    api.post<TestNotificationResponse>('/api/settings/notifications/test', req),

  testExisting: (id: number) =>
    api.post<TestNotificationResponse>(`/api/settings/notifications/${id}/test`),
}
