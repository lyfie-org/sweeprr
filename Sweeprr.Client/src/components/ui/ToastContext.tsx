import { createContext, useCallback, useContext, useRef, useState } from 'react'
import { CheckCircle, Warning, XCircle, Info, X } from '@phosphor-icons/react'
import './Toast.css'

type ToastType = 'success' | 'warning' | 'error' | 'info'

interface ToastItem {
  id: string
  type: ToastType
  title: string
  message?: string
  duration?: number
}

interface ToastContextValue {
  toast: (options: Omit<ToastItem, 'id'>) => void
  dismiss: (id: string) => void
}

const ToastContext = createContext<ToastContextValue | null>(null)

const ICONS: Record<ToastType, React.ReactNode> = {
  success: <CheckCircle size={18} weight="fill" />,
  warning: <Warning size={18} weight="fill" />,
  error:   <XCircle size={18} weight="fill" />,
  info:    <Info size={18} weight="fill" />,
}

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<(ToastItem & { exiting?: boolean })[]>([])
  const timers = useRef(new Map<string, ReturnType<typeof setTimeout>>())

  const dismiss = useCallback((id: string) => {
    setToasts(prev => prev.map(t => t.id === id ? { ...t, exiting: true } : t))
    const timer = timers.current.get(id)
    if (timer) clearTimeout(timer)
    timers.current.delete(id)
    setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 200)
  }, [])

  const toast = useCallback((options: Omit<ToastItem, 'id'>) => {
    const id = Math.random().toString(36).slice(2)
    const duration = options.duration ?? 4000
    setToasts(prev => [...prev, { ...options, id }])
    if (duration > 0) {
      timers.current.set(id, setTimeout(() => dismiss(id), duration))
    }
  }, [dismiss])

  return (
    <ToastContext.Provider value={{ toast, dismiss }}>
      {children}
      <div
        className="toast-container"
        role="region"
        aria-live="polite"
        aria-label="Notifications"
      >
        {toasts.map(t => (
          <div
            key={t.id}
            className={`toast toast--${t.type}${t.exiting ? ' toast--exiting' : ''}`}
            role="alert"
          >
            <div className="toast__accent-bar" aria-hidden="true" />
            <span className="toast__icon" aria-hidden="true">{ICONS[t.type]}</span>
            <div className="toast__content">
              <div className="toast__title">{t.title}</div>
              {t.message && <div className="toast__message">{t.message}</div>}
            </div>
            <button
              className="toast__dismiss"
              onClick={() => dismiss(t.id)}
              aria-label="Dismiss notification"
            >
              <X size={14} weight="bold" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast must be used within ToastProvider')
  return ctx
}
