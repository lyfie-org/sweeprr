import { type FormEvent, type ReactNode, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Broom, CheckCircle, Clock, WarningCircle } from '@phosphor-icons/react'
import {
  ExtendApiError,
  clearExtendToken,
  extendApi,
  getExtendToken,
  getExtendUsername,
  setExtendToken,
  type MediaStatusResponse,
} from '../api/extendClient'
import { Button, Input, Spinner } from '../components/ui'
import './ExtendPage.css'

const EXTEND_DAYS = 14

export function ExtendPage() {
  const [searchParams] = useSearchParams()
  const itemId = searchParams.get('itemId')

  const [status, setStatus] = useState<MediaStatusResponse | null>(null)
  const [statusLoading, setStatusLoading] = useState(true)
  const [statusError, setStatusError] = useState<string | null>(null)

  const [token, setToken] = useState(getExtendToken())
  const [username, setUsername] = useState(getExtendUsername())

  const [loginUsername, setLoginUsername] = useState('')
  const [loginPassword, setLoginPassword] = useState('')
  const [loginError, setLoginError] = useState<string | null>(null)
  const [signingIn, setSigningIn] = useState(false)

  const [extending, setExtending] = useState(false)
  const [extendError, setExtendError] = useState<string | null>(null)
  const [newExpiresAt, setNewExpiresAt] = useState<string | null>(null)

  useEffect(() => {
    if (!itemId) {
      setStatusLoading(false)
      return
    }
    extendApi.getStatus(itemId)
      .then(setStatus)
      .catch(() => setStatusError('Could not load this item.'))
      .finally(() => setStatusLoading(false))
  }, [itemId])

  const handleLogin = async (e: FormEvent) => {
    e.preventDefault()
    setLoginError(null)
    setSigningIn(true)
    try {
      const res = await extendApi.login(loginUsername, loginPassword)
      setExtendToken(res.accessToken, res.username)
      setToken(res.accessToken)
      setUsername(res.username)
    } catch (err) {
      setLoginError(err instanceof ExtendApiError ? err.message : 'Sign in failed.')
    } finally {
      setSigningIn(false)
    }
  }

  const handleExtend = async () => {
    if (!itemId) return
    setExtendError(null)
    setExtending(true)
    try {
      const res = await extendApi.extend(itemId, EXTEND_DAYS)
      if (res.success) {
        setNewExpiresAt(res.newExpiresAt)
      } else {
        setExtendError(res.error ?? 'Could not extend this item.')
      }
    } catch (err) {
      if (err instanceof ExtendApiError && err.status === 401) {
        clearExtendToken()
        setToken(null)
        setUsername(null)
        setExtendError('Your session expired. Please sign in again.')
      } else {
        setExtendError(err instanceof ExtendApiError ? err.message : 'Could not extend this item.')
      }
    } finally {
      setExtending(false)
    }
  }

  let content: ReactNode

  if (!itemId) {
    content = (
      <div className="extend-card__error" role="alert">
        <WarningCircle size={16} weight="fill" />
        Missing item link. Please use the link from Jellyfin.
      </div>
    )
  } else if (statusLoading) {
    content = (
      <div className="extend-loading">
        <Spinner size="lg" />
      </div>
    )
  } else if (statusError) {
    content = (
      <div className="extend-card__error" role="alert">
        <WarningCircle size={16} weight="fill" />
        {statusError}
      </div>
    )
  } else {
    content = (
      <>
        {status?.posterUrl && (
          <img className="extend-poster" src={status.posterUrl} alt="" />
        )}
        <h2 className="extend-title">{status?.title ?? 'Unknown item'}</h2>

        {status?.isQueued ? (
          <p className="extend-removal">
            <Clock size={16} weight="duotone" />
            Scheduled for removal in {status.daysRemaining ?? '?'} day{status.daysRemaining === 1 ? '' : 's'}
          </p>
        ) : (
          <p className="extend-removal extend-removal--ok">
            <CheckCircle size={16} weight="duotone" />
            This item isn't scheduled for removal.
          </p>
        )}

        {newExpiresAt ? (
          <div className="extend-success" role="status">
            <CheckCircle size={20} weight="duotone" />
            <span>
              Extended! This item is safe until{' '}
              {new Date(newExpiresAt).toLocaleDateString()}.
            </span>
          </div>
        ) : !token ? (
          <form className="extend-card__form" onSubmit={handleLogin} noValidate>
            <p className="extend-form__hint">Sign in with your Jellyfin account to request an extension.</p>

            {loginError && (
              <div className="extend-card__error" role="alert">
                <WarningCircle size={16} weight="fill" />
                {loginError}
              </div>
            )}

            <Input
              label="Username"
              value={loginUsername}
              onChange={e => setLoginUsername(e.target.value)}
              autoComplete="username"
              required
              disabled={signingIn}
            />

            <Input
              label="Password"
              type="password"
              value={loginPassword}
              onChange={e => setLoginPassword(e.target.value)}
              autoComplete="current-password"
              required
              disabled={signingIn}
            />

            <Button type="submit" variant="primary" size="lg" loading={signingIn} style={{ width: '100%' }}>
              Sign in
            </Button>
          </form>
        ) : (
          <div className="extend-card__form">
            <p className="extend-form__hint">
              Signed in as <strong>{username}</strong>
            </p>

            {extendError && (
              <div className="extend-card__error" role="alert">
                <WarningCircle size={16} weight="fill" />
                {extendError}
              </div>
            )}

            <Button
              variant="primary"
              size="lg"
              loading={extending}
              disabled={!status?.isQueued}
              onClick={handleExtend}
              style={{ width: '100%' }}
            >
              Extend by {EXTEND_DAYS} days
            </Button>
          </div>
        )}
      </>
    )
  }

  return (
    <div className="extend-screen">
      <div className="extend-card">
        <div className="extend-card__header">
          <div className="extend-card__logo">
            <Broom size={32} weight="duotone" color="var(--accent)" />
          </div>
          <h1 className="extend-card__title">Sweeprr</h1>
          <p className="extend-card__subtitle">Request more time before this item is removed</p>
        </div>

        {content}
      </div>
    </div>
  )
}
