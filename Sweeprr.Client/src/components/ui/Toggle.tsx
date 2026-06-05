import { useId } from 'react'
import './Toggle.css'

interface ToggleProps {
  checked: boolean
  onChange: (checked: boolean) => void
  label?: string
  description?: string
  disabled?: boolean
  id?: string
}

export function Toggle({
  checked,
  onChange,
  label,
  description,
  disabled = false,
  id,
}: ToggleProps) {
  const autoId = useId()
  const inputId = id ?? autoId

  return (
    <label
      className={`toggle-root ${disabled ? 'toggle-root--disabled' : ''}`}
      htmlFor={inputId}
    >
      <input
        id={inputId}
        type="checkbox"
        role="switch"
        className="toggle__input"
        checked={checked}
        disabled={disabled}
        onChange={e => onChange(e.target.checked)}
        aria-checked={checked}
      />
      <span className="toggle__track" aria-hidden="true">
        <span className="toggle__thumb" />
      </span>
      {(label || description) && (
        <span className="toggle__text">
          {label && <span className="toggle__label">{label}</span>}
          {description && <span className="toggle__description">{description}</span>}
        </span>
      )}
    </label>
  )
}
