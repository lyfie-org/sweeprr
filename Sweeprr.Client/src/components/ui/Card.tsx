import './Card.css'

type CardVariant = 'default' | 'elevated' | 'flat'

interface CardProps {
  variant?: CardVariant
  className?: string
  children: React.ReactNode
  as?: React.ElementType
}

interface CardHeaderProps {
  title?: string
  children?: React.ReactNode
  actions?: React.ReactNode
  className?: string
}

export function Card({ variant = 'default', className = '', children, as: As = 'div' }: CardProps) {
  return (
    <As className={`card card--${variant} ${className}`}>
      {children}
    </As>
  )
}

export function CardHeader({ title, children, actions, className = '' }: CardHeaderProps) {
  return (
    <div className={`card__header ${className}`}>
      <div>
        {title && <h2 className="card__title">{title}</h2>}
        {children}
      </div>
      {actions && <div>{actions}</div>}
    </div>
  )
}

export function CardBody({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return <div className={`card__body ${className}`}>{children}</div>
}

export function CardFooter({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return <div className={`card__footer ${className}`}>{children}</div>
}
