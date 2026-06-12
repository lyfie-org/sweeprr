const TOKEN_KEY = 'sweeprr_token'

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

export function setToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token)
}

export function clearToken(): void {
  localStorage.removeItem(TOKEN_KEY)
}

export class ApiError extends Error {
  readonly status: number
  readonly body: unknown

  constructor(status: number, message: string, body?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

export const AUTH_EXPIRED_EVENT = 'sweeprr:auth-expired'

interface RequestOptions extends RequestInit {
  /** When true, a 401 response will NOT dispatch AUTH_EXPIRED_EVENT.
   *  Use for login/setup calls where a 401 means wrong credentials, not session expiry. */
  skipAuthEvent?: boolean
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { skipAuthEvent, ...fetchOptions } = options
  const token = getToken()
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(fetchOptions.headers as Record<string, string>),
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  }

  const res = await fetch(path, { ...fetchOptions, headers })

  if (res.status === 401) {
    if (!skipAuthEvent) {
      clearToken()
      window.dispatchEvent(new Event(AUTH_EXPIRED_EVENT))
    }
    let body: unknown
    try { body = await res.json() } catch { /* empty */ }
    const message = skipAuthEvent
      ? ((body as { error?: string })?.error ?? 'Invalid credentials')
      : 'Session expired'
    throw new ApiError(401, message, body)
  }

  if (!res.ok) {
    let body: unknown
    try { body = await res.json() } catch { /* empty */ }
    const message =
      (body as { error?: string })?.error ??
      (body as { title?: string })?.title ??
      `HTTP ${res.status}`
    throw new ApiError(res.status, message, body)
  }

  if (res.status === 204) return undefined as T

  const text = await res.text()
  if (!text) return undefined as T
  return JSON.parse(text) as T
}

export const api = {
  get:    <T>(path: string)                  => request<T>(path),
  post:   <T>(path: string, body?: unknown)  => request<T>(path, { method: 'POST',   body: body !== undefined ? JSON.stringify(body) : undefined }),
  put:    <T>(path: string, body?: unknown)  => request<T>(path, { method: 'PUT',    body: body !== undefined ? JSON.stringify(body) : undefined }),
  patch:  <T>(path: string, body?: unknown)  => request<T>(path, { method: 'PATCH',  body: body !== undefined ? JSON.stringify(body) : undefined }),
  delete: <T>(path: string)                  => request<T>(path, { method: 'DELETE' }),
  /** Like post(), but a 401 response shows the server's error message instead of 'Session expired'. */
  postAnon: <T>(path: string, body?: unknown) => request<T>(path, { method: 'POST', body: body !== undefined ? JSON.stringify(body) : undefined, skipAuthEvent: true }),
}
