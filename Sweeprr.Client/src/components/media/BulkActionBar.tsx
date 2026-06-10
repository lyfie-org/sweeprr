import { CalendarX, Prohibit, X } from '@phosphor-icons/react'
import { Button } from '../ui'
import './BulkActionBar.css'

interface BulkActionBarProps {
  count: number
  busy?: boolean
  onScheduleLeavingSoon: () => void
  onAddExclusion: () => void
  onClear: () => void
}

export function BulkActionBar({
  count,
  busy = false,
  onScheduleLeavingSoon,
  onAddExclusion,
  onClear,
}: BulkActionBarProps) {
  return (
    <div className="media-bulk-bar">
      <span className="media-bulk-bar__count">{count} item{count === 1 ? '' : 's'} selected</span>
      <Button
        variant="primary"
        size="sm"
        loading={busy}
        iconLeft={<CalendarX size={16} weight="duotone" />}
        onClick={onScheduleLeavingSoon}
      >
        Schedule Leaving Soon
      </Button>
      <Button
        variant="secondary"
        size="sm"
        disabled={busy}
        iconLeft={<Prohibit size={16} weight="duotone" />}
        onClick={onAddExclusion}
      >
        Add Exclusion
      </Button>
      <button
        className="media-bulk-bar__clear"
        onClick={onClear}
        disabled={busy}
        aria-label="Clear selection"
      >
        <X size={16} weight="bold" />
      </button>
    </div>
  )
}
