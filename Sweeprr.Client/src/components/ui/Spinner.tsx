import './Spinner.css'

type SpinnerSize = 'sm' | 'md' | 'lg' | 'xl'
type SpinnerColor = 'accent' | 'white' | 'muted'

interface SpinnerProps {
  size?: SpinnerSize
  color?: SpinnerColor
  className?: string
}

export function Spinner({ size = 'md', color = 'accent', className = '' }: SpinnerProps) {
  return (
    <span
      className={`spinner spinner--${size} spinner--${color} ${className}`}
      role="status"
      aria-label="Loading"
    />
  )
}
