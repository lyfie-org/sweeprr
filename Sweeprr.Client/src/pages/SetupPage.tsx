import { type FormEvent, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Broom, WarningCircle } from '@phosphor-icons/react'
import { useAuth } from '../context/AuthContext'
import { ApiError } from '../api/client'
import { Button, Input, Spinner } from '../components/ui'
import './LoginPage.css'

export function SetupPage() {
  const { user, isLoading, isFirstRun, setup } = useAuth()
  const navigate = useNavigate()

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm]   = useState('')
  const [error, setError]       = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    if (!isLoading && isFirstRun === false && !user) navigate('/login', { replace: true })
    if (!isLoading && user) navigate('/', { replace: true })
  }, [isLoading, isFirstRun, user, navigate])

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

    if (password !== confirm) {
      setError('Passwords do not match')
      return
    }
    if (password.length < 8) {
      setError('Password must be at least 8 characters')
      return
    }

    setSubmitting(true)
    try {
      await setup(username, password)
      navigate('/', { replace: true })
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Setup failed')
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
          <h1 className="auth-card__title">Welcome to Sweeprr</h1>
          <p className="auth-card__subtitle">Create your admin account to get started</p>
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
            autoComplete="new-password"
            helper="At least 8 characters"
            required
            disabled={submitting}
          />

          <Input
            label="Confirm password"
            type="password"
            value={confirm}
            onChange={e => setConfirm(e.target.value)}
            autoComplete="new-password"
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
            Create account
          </Button>
        </form>
      </div>
    </div>
  )
}
