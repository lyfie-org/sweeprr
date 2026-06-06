import { useEffect, useState } from 'react'
import {
  HardDrives,
  Broom,
  Stack,
  Lightning,
  Warning,
  CheckCircle,
  Info,
  XCircle,
  Clock,
  ChartLine,
} from '@phosphor-icons/react'
import { dashboardApi, type DashboardStats, type ActivityLogEntry, type SparklinePoint } from '../api/dashboard'
import { Card, CardBody, CardHeader } from '../components/ui/Card'
import { Badge } from '../components/ui/Badge'
import { Sparkline } from '../components/ui/Sparkline'
import './DashboardPage.css'

// ── Formatters ────────────────────────────────────────────────────────────────

function formatGb(gb: number): string {
  if (gb >= 1000) return `${(gb / 1000).toFixed(1)} TB`
  if (gb >= 1)    return `${gb.toFixed(1)} GB`
  if (gb > 0)     return `${(gb * 1000).toFixed(0)} MB`
  return '0 GB'
}

function formatCount(n: number): string {
  return n.toLocaleString()
}

function formatRelativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  const m = Math.floor(diff / 60_000)
  if (m < 1)   return 'just now'
  if (m < 60)  return `${m}m ago`
  const h = Math.floor(m / 60)
  if (h < 24)  return `${h}h ago`
  return `${Math.floor(h / 24)}d ago`
}

function formatNextRun(iso: string | null): string {
  if (!iso) return '—'
  const diff = new Date(iso).getTime() - Date.now()
  if (diff < 0) return 'imminently'
  const m = Math.floor(diff / 60_000)
  if (m < 60) return `in ${m}m`
  const h = Math.floor(m / 60)
  if (h < 24) return `in ${h}h ${m % 60}m`
  return `in ${Math.floor(h / 24)}d`
}

// ── WS state helpers ──────────────────────────────────────────────────────────

function wsLabel(state: DashboardStats['wsState']): string {
  switch (state) {
    case 'Connected':    return 'Connected'
    case 'Connecting':   return 'Connecting'
    case 'Reconnecting': return 'Reconnecting'
    default:             return 'Offline'
  }
}

function wsClass(state: DashboardStats['wsState']): string {
  switch (state) {
    case 'Connected':    return 'stat-card__value--connected'
    case 'Reconnecting':
    case 'Connecting':   return 'stat-card__value--reconnecting'
    default:             return 'stat-card__value--offline'
  }
}

// ── Activity helpers ──────────────────────────────────────────────────────────

type BadgeVariant = 'success' | 'warning' | 'danger' | 'info' | 'neutral' | 'accent'

function categoryVariant(cat: ActivityLogEntry['category']): BadgeVariant {
  switch (cat) {
    case 'Sweep':      return 'accent'
    case 'Connection': return 'info'
    case 'Rule':       return 'neutral'
    case 'Auth':       return 'warning'
    default:           return 'neutral'
  }
}

function levelIcon(level: ActivityLogEntry['level']) {
  switch (level) {
    case 'Error':       return <XCircle size={14} weight="fill" color="var(--danger)"  />
    case 'Warning':     return <Warning  size={14} weight="fill" color="var(--warning)" />
    case 'Information': return <Info     size={14} weight="fill" color="var(--info)"    />
    default:            return <Info     size={14} weight="fill" color="var(--text-muted)" />
  }
}

// ── Sparkline label helpers ───────────────────────────────────────────────────

function sparklineEdgeDates(points: SparklinePoint[]): { first: string; last: string } {
  if (points.length === 0) return { first: '', last: '' }
  const fmt = (d: string) => {
    const dt = new Date(d)
    return dt.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
  }
  return { first: fmt(points[0].date), last: fmt(points[points.length - 1].date) }
}

// ── Main component ────────────────────────────────────────────────────────────

export function DashboardPage() {
  const [stats, setStats]       = useState<DashboardStats | null>(null)
  const [activity, setActivity] = useState<ActivityLogEntry[]>([])
  const [sparkline, setSparkline] = useState<SparklinePoint[]>([])
  const [loading, setLoading]   = useState(true)

  useEffect(() => {
    let cancelled = false

    async function load() {
      try {
        const [s, a, sp] = await Promise.all([
          dashboardApi.getStats(),
          dashboardApi.getActivity(20),
          dashboardApi.getSparkline(30),
        ])
        if (!cancelled) {
          setStats(s)
          setActivity(a)
          setSparkline(sp)
        }
      } catch {
        // errors shown via toast in a real scenario; silent here for skeleton → empty
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    load()
    return () => { cancelled = true }
  }, [])

  const { first: sparkFirst, last: sparkLast } = sparklineEdgeDates(sparkline)

  return (
    <div>
      <div className="dashboard__header">
        <h1 className="dashboard__title">Dashboard</h1>
      </div>

      {/* Dry-run banner */}
      {!loading && stats?.globalDryRun && (
        <div className="dashboard__dry-run-banner">
          <Warning size={16} weight="fill" />
          <span><strong>Simulation mode is on.</strong> Sweeps will be logged but nothing will be deleted until you disable Dry Run in Settings.</span>
        </div>
      )}

      {/* Stat cards */}
      <div className="dashboard__stat-grid">
        <StatCard
          icon={<HardDrives size={20} weight="duotone" />}
          label="Storage Recovered"
          value={loading ? null : formatGb(stats?.totalGbRecovered ?? 0)}
          sub={loading ? null : `${formatGb(stats?.gbRecoveredLast30d ?? 0)} last 30 days`}
        />
        <StatCard
          icon={<Broom size={20} weight="duotone" />}
          label="Items Swept"
          value={loading ? null : formatCount(stats?.totalItemsSwept ?? 0)}
          sub={loading ? null : `${formatCount(stats?.itemsSweptLast30d ?? 0)} last 30 days`}
        />
        <StatCard
          icon={<Stack size={20} weight="duotone" />}
          label="Pending Queue"
          value={loading ? null : formatCount(stats?.pendingQueueCount ?? 0)}
          sub={loading ? null : stats?.pendingQueueCount
            ? 'items awaiting review'
            : 'queue is clear'}
        />
        <StatCard
          icon={<Lightning size={20} weight="duotone" />}
          label="Realtime"
          value={loading ? null : wsLabel(stats?.wsState ?? 'Disconnected')}
          valueClass={loading ? undefined : wsClass(stats?.wsState ?? 'Disconnected')}
          sub={loading ? null : `Next run ${formatNextRun(stats?.nextScheduledRun ?? null)}`}
        />
      </div>

      {/* Bottom row: sparkline + activity */}
      <div className="dashboard__bottom">
        {/* Sparkline */}
        <Card>
          <CardBody className="stat-card">
            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
              <ChartLine size={16} weight="duotone" color="var(--accent)" />
              <span style={{ fontSize: 'var(--text-xs)', fontWeight: 'var(--font-weight-semibold)', textTransform: 'uppercase', letterSpacing: '0.08em', color: 'var(--text-muted)' }}>
                Storage Recovered — Last 30 Days
              </span>
            </div>
            <div className="sparkline-card__chart">
              {loading
                ? <div className="dashboard__skeleton" style={{ height: 80 }} />
                : <Sparkline points={sparkline} height={80} />}
              {!loading && (
                <div className="sparkline-card__legend">
                  <span>{sparkFirst}</span>
                  <span>{sparkLast}</span>
                </div>
              )}
            </div>
          </CardBody>
        </Card>

        {/* Recent activity */}
        <Card>
          <CardHeader title="Recent Activity" />
          <CardBody>
            {loading ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-3)' }}>
                {[...Array(5)].map((_, i) => (
                  <div key={i} className="dashboard__skeleton" style={{ width: `${70 + (i % 3) * 10}%` }} />
                ))}
              </div>
            ) : activity.length === 0 ? (
              <div className="dashboard__empty">
                <CheckCircle size={32} weight="duotone" />
                <p style={{ margin: 0, fontSize: 'var(--text-sm)' }}>No activity yet — create your first rule to get started.</p>
              </div>
            ) : (
              <div className="activity-feed">
                {activity.map(entry => (
                  <div key={entry.id} className="activity-item">
                    <span className="activity-item__icon">{levelIcon(entry.level)}</span>
                    <div className="activity-item__body">
                      <Badge variant={categoryVariant(entry.category)} size="sm">
                        {entry.category}
                      </Badge>
                      <span className="activity-item__message" style={{ marginLeft: 'var(--space-2)' }}>
                        {entry.message}
                      </span>
                    </div>
                    <span className="activity-item__time">
                      <Clock size={11} style={{ verticalAlign: 'middle', marginRight: 3 }} />
                      {formatRelativeTime(entry.timestamp)}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </CardBody>
        </Card>
      </div>
    </div>
  )
}

// ── StatCard sub-component ────────────────────────────────────────────────────

interface StatCardProps {
  icon: React.ReactNode
  label: string
  value: string | null
  sub: string | null
  valueClass?: string
}

function StatCard({ icon, label, value, sub, valueClass }: StatCardProps) {
  return (
    <Card>
      <CardBody className="stat-card">
        <div className="stat-card__icon">{icon}</div>
        <p className="stat-card__label">{label}</p>
        {value === null
          ? <div className="dashboard__skeleton" style={{ height: 36, width: '60%' }} />
          : <p className={`stat-card__value${valueClass ? ` ${valueClass}` : ''}`}>{value}</p>}
        {sub === null
          ? <div className="dashboard__skeleton" style={{ height: 14, width: '80%' }} />
          : <p className="stat-card__sub">{sub}</p>}
      </CardBody>
    </Card>
  )
}
