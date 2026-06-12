// Separate client for the public "Request Extension" portal (Story 10.4).
// Uses sessionStorage under its own key — must never share state with the
// admin client.ts (which uses localStorage + AUTH_EXPIRED_EVENT).

const TOKEN_KEY = 'sweeprr_extend_token'
const USERNAME_KEY = 'sweeprr_extend_username'

export function getExtendToken(): string | null {
  return sessionStorage.getItem(TOKEN_KEY)
}

export function getExtendUsername(): string | null {
  return sessionStorage.getItem(USERNAME_KEY)
}

export function setExtendToken(token: string, username: string): void {
  sessionStorage.setItem(TOKEN_KEY, token)
  sessionStorage.setItem(USERNAME_KEY, username)
}

export function clearExtendToken(): void {
  sessionStorage.removeItem(TOKEN_KEY)
  sessionStorage.removeItem(USERNAME_KEY)
}

export class ExtendApiError extends Error {
  readonly status: number
  readonly body: unknown

  constructor(status: number, message: string, body?: unknown) {
    super(message)
    this.name = 'ExtendApiError'
    this.status = status
    this.body = body
  }
}

async function request<T>(path: string, options: RequestInit = {}, authed = false): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string>),
  }

  if (authed) {
    const token = getExtendToken()
    if (token) headers.Authorization = `Bearer ${token}`
  }

  const res = await fetch(path, { ...options, headers })

  if (!res.ok) {
    let body: unknown
    try { body = await res.json() } catch { /* empty */ }
    const message =
      (body as { error?: string })?.error ??
      (body as { title?: string })?.title ??
      `HTTP ${res.status}`
    throw new ExtendApiError(res.status, message, body)
  }

  if (res.status === 204) return undefined as T

  const text = await res.text()
  if (!text) return undefined as T
  return JSON.parse(text) as T
}

export interface MediaStatusResponse {
  isQueued: boolean
  daysRemaining: number | null
  title: string | null
  posterUrl: string | null
}

export interface ExtensionPortalTokenResponse {
  accessToken: string
  expiresAt: string
  username: string
}

export interface ExtendResponse {
  success: boolean
  newExpiresAt: string | null
  error: string | null
}

export const extendApi = {
  getStatus: (itemId: string) =>
    request<MediaStatusResponse>(`/api/public/media/${encodeURIComponent(itemId)}/status`),

  login: (username: string, password: string) =>
    request<ExtensionPortalTokenResponse>('/api/public/auth/jellyfin', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  extend: (jellyfinItemId: string, requestedDays = 14) =>
    request<ExtendResponse>('/api/public/extend', {
      method: 'POST',
      body: JSON.stringify({ jellyfinItemId, requestedDays }),
    }, true),
}
