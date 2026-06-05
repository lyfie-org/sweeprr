import { useState } from 'react'
import { Eye, EyeSlash } from '@phosphor-icons/react'
import './Input.css'

interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string
  error?: string
  helper?: string
  iconLeft?: React.ReactNode
}

export function Input({
  label,
  error,
  helper,
  iconLeft,
  type = 'text',
  id,
  required,
  className = '',
  ...props
}: InputProps) {
  const [showPassword, setShowPassword] = useState(false)
  const isPassword = type === 'password'
  const inputId = id ?? label?.toLowerCase().replace(/\s+/g, '-')
  const resolvedType = isPassword ? (showPassword ? 'text' : 'password') : type

  const fieldCls = [
    'input__field',
    iconLeft ? 'input__field--has-left' : '',
    isPassword ? 'input__field--has-right' : '',
  ].filter(Boolean).join(' ')

  return (
    <div className={`input-root ${error ? 'input--error' : ''} ${className}`}>
      {label && (
        <label
          htmlFor={inputId}
          className={`input__label ${required ? 'input__label--required' : ''}`}
        >
          {label}
        </label>
      )}
      <div className="input__wrapper">
        {iconLeft && (
          <span className="input__icon-left" aria-hidden="true">
            {iconLeft}
          </span>
        )}
        <input
          id={inputId}
          type={resolvedType}
          className={fieldCls}
          required={required}
          aria-invalid={error ? true : undefined}
          aria-describedby={
            error ? `${inputId}-error`
            : helper ? `${inputId}-helper`
            : undefined
          }
          {...props}
        />
        {isPassword && (
          <button
            type="button"
            className="input__icon-right"
            onClick={() => setShowPassword(v => !v)}
            aria-label={showPassword ? 'Hide password' : 'Show password'}
            tabIndex={-1}
          >
            {showPassword ? <EyeSlash size={16} /> : <Eye size={16} />}
          </button>
        )}
      </div>
      {error && (
        <span id={`${inputId}-error`} className="input__error-msg" role="alert">
          {error}
        </span>
      )}
      {!error && helper && (
        <span id={`${inputId}-helper`} className="input__helper">
          {helper}
        </span>
      )}
    </div>
  )
}
