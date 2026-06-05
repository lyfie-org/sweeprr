import './Button.css'

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'
type ButtonSize = 'sm' | 'md' | 'lg'

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  loading?: boolean
  iconLeft?: React.ReactNode
  iconRight?: React.ReactNode
}

export function Button({
  variant = 'primary',
  size = 'md',
  loading = false,
  iconLeft,
  iconRight,
  children,
  disabled,
  className = '',
  ...props
}: ButtonProps) {
  const cls = [
    'btn',
    `btn--${variant}`,
    `btn--${size}`,
    loading ? 'btn--loading' : '',
    className,
  ].filter(Boolean).join(' ')

  return (
    <button
      className={cls}
      disabled={disabled || loading}
      aria-disabled={disabled || loading}
      {...props}
    >
      {loading
        ? <span className="btn__spinner" aria-hidden="true" />
        : iconLeft}
      {children}
      {iconRight}
    </button>
  )
}
