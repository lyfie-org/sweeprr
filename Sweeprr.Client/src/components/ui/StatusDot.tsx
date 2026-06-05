import './StatusDot.css'

type StatusType = 'connected' | 'disconnected' | 'pending'
type DotSize = 'sm' | 'md' | 'lg'

interface StatusDotProps {
  status: StatusType
  size?: DotSize
  label?: string
}

const STATUS_LABELS: Record<StatusType, string> = {
  connected:    'Connected',
  disconnected: 'Disconnected',
  pending:      'Connecting',
}

export function StatusDot({ status, size = 'md', label }: StatusDotProps) {
  const displayLabel = label ?? STATUS_LABELS[status]
  return (
    <span
      className={`status-dot status-dot--${status} status-dot--${size}`}
      role="status"
      aria-label={displayLabel}
    >
      <span className="status-dot__indicator" aria-hidden="true" />
      {label !== undefined && <span>{displayLabel}</span>}
    </span>
  )
}
