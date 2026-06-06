import { Article } from '@phosphor-icons/react'
import { Card, CardBody } from '../components/ui'

export function LogsPage() {
  return (
    <div>
      <h1 style={{ fontSize: 'var(--text-2xl)', fontWeight: 'var(--font-weight-bold)', color: 'var(--text-primary)', marginBottom: 'var(--space-6)' }}>
        Logs
      </h1>
      <Card>
        <CardBody>
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 'var(--space-4)', padding: 'var(--space-10)', color: 'var(--text-muted)', textAlign: 'center' }}>
            <Article size={40} weight="duotone" />
            <p style={{ margin: 0 }}>No activity logged yet.</p>
          </div>
        </CardBody>
      </Card>
    </div>
  )
}
