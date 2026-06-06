import { type FormEvent, useEffect, useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { Broom, WarningCircle } from '@phosphor-icons/react'
import { useAuth } from '../context/AuthContext'
import { ApiError } from '../api/client'
import { Button, Input, Spinner } from '../components/ui'
import './LoginPage.css'

export function LoginPage() {
  const { user, isLoading, isFirstRun, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError]       = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const from = (location.state as { from?: Location })?.from?.pathname ?? '/'

  useEffect(() => {
    if (!isLoading && isFirstRun)   navigate('/setup', { replace: true })
    if (!isLoading && user)         navigate(from, { replace: true })
  }, [isLoading, isFirstRun, user, navigate, from])

  if (isLoading) {
    return (
      <div className="auth-screen">
        <Spinner size="xl" />
      </div>
    )
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    setSubmitting(true)
    try {
      await login(username, password)
      navigate(from, { replace: true })
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Login failed')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="auth-screen">
      <div className="auth-card">
        <div className="auth-card__header">
          <div className="auth-card__logo">
            <Broom size={32} weight="duotone" color="var(--accent)" />
          </div>
          <h1 className="auth-card__title">Welcome back</h1>
          <p className="auth-card__subtitle">Sign in to your Sweeprr instance</p>
        </div>

        <form className="auth-card__form" onSubmit={handleSubmit} noValidate>
          {error && (
            <div className="auth-card__error" role="alert">
              <WarningCircle size={16} weight="fill" />
              {error}
            </div>
          )}

          <Input
            label="Username"
            value={username}
            onChange={e => setUsername(e.target.value)}
            autoComplete="username"
            required
            disabled={submitting}
          />

          <Input
            label="Password"
            type="password"
            value={password}
            onChange={e => setPassword(e.target.value)}
            autoComplete="current-password"
            required
            disabled={submitting}
          />

          <Button
            type="submit"
            variant="primary"
            size="lg"
            loading={submitting}
            style={{ width: '100%', marginTop: 'var(--space-2)' }}
          >
            Sign in
          </Button>
        </form>
      </div>
    </div>
  )
}
