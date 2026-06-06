import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react'
import { api, ApiError, AUTH_EXPIRED_EVENT, clearToken, getToken, setToken } from '../api/client'

interface AuthUser {
  id: number
  username: string
  role: string
}

interface AuthTokenResponse {
  accessToken: string
  expiresAt: string
  username: string
}

interface MeResponse {
  id: number
  username: string
  role: string
}

interface AuthStatusResponse {
  isFirstRun: boolean
}

interface AuthContextValue {
  user: AuthUser | null
  isLoading: boolean
  isFirstRun: boolean | null
  login: (username: string, password: string) => Promise<void>
  setup: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isFirstRun, setIsFirstRun] = useState<boolean | null>(null)
  const initialized = useRef(false)

  const fetchStatus = useCallback(async () => {
    try {
      const status = await api.get<AuthStatusResponse>('/api/auth/status')
      setIsFirstRun(status.isFirstRun)
    } catch {
      setIsFirstRun(false)
    }
  }, [])

  const fetchMe = useCallback(async () => {
    if (!getToken()) return
    try {
      const me = await api.get<MeResponse>('/api/auth/me')
      setUser({ id: me.id, username: me.username, role: me.role })
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) clearToken()
    }
  }, [])

  useEffect(() => {
    if (initialized.current) return
    initialized.current = true

    Promise.all([fetchStatus(), fetchMe()]).finally(() => setIsLoading(false))

    const onExpired = () => setUser(null)
    window.addEventListener(AUTH_EXPIRED_EVENT, onExpired)
    return () => window.removeEventListener(AUTH_EXPIRED_EVENT, onExpired)
  }, [fetchStatus, fetchMe])

  const login = useCallback(async (username: string, password: string) => {
    const res = await api.post<AuthTokenResponse>('/api/auth/login', { username, password })
    setToken(res.accessToken)
    const me = await api.get<MeResponse>('/api/auth/me')
    setUser({ id: me.id, username: me.username, role: me.role })
    setIsFirstRun(false)
  }, [])

  const setup = useCallback(async (username: string, password: string) => {
    const res = await api.post<AuthTokenResponse>('/api/auth/setup', { username, password })
    setToken(res.accessToken)
    const me = await api.get<MeResponse>('/api/auth/me')
    setUser({ id: me.id, username: me.username, role: me.role })
    setIsFirstRun(false)
  }, [])

  const logout = useCallback(() => {
    clearToken()
    setUser(null)
  }, [])

  return (
    <AuthContext.Provider value={{ user, isLoading, isFirstRun, login, setup, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
