import { useState } from 'react'
import {
  MagnifyingGlass,
  HardDrives,
  Broom,
  Lightning,
  PlugsConnected,
  Gear,
} from '@phosphor-icons/react'
import {
  Button,
  Card, CardHeader, CardBody, CardFooter,
  Badge,
  Input,
  Toggle,
  Modal,
  Table, TableHead, TableBody, TableRow, TableCell, TableHeaderCell, TableEmpty,
  StatusDot,
  Spinner,
  useToast,
} from '../components/ui'
import './KitchenSink.css'

const DEMO_ROWS = [
  { title: 'The Dark Knight',      type: 'Movie',  size: '18.4 GB', status: 'Pending',  why: 'Last watched 92d ago' },
  { title: 'Breaking Bad S5',      type: 'Season', size: '24.1 GB', status: 'Approved', why: 'All users watched' },
  { title: 'Inception',            type: 'Movie',  size: '11.2 GB', status: 'Ignored',  why: 'Rating < 7' },
]

const STATUS_BADGE: Record<string, 'warning' | 'success' | 'neutral'> = {
  Pending:  'warning',
  Approved: 'success',
  Ignored:  'neutral',
}

export function KitchenSink() {
  const [modalOpen, setModalOpen]     = useState(false)
  const [dryRun, setDryRun]           = useState(true)
  const [loading, setLoading]         = useState(false)
  const [inputVal, setInputVal]       = useState('')
  const { toast } = useToast()

  const triggerLoad = () => {
    setLoading(true)
    setTimeout(() => setLoading(false), 1800)
  }

  return (
    <div className="ks">
      <header className="ks__header">
        <h1 className="ks__title">Kitchen Sink</h1>
        <p className="ks__subtitle">Design system preview — all primitive components</p>
      </header>

      {/* Colors */}
      <section className="ks__section">
        <p className="ks__section-title">Color Tokens</p>
        <div className="ks__color-grid">
          {[
            ['--accent',  '#6c63ff', 'accent'],
            ['--success', '#22c55e', 'success'],
            ['--warning', '#f59e0b', 'warning'],
            ['--danger',  '#ef4444', 'danger'],
            ['--info',    '#3b82f6', 'info'],
          ].map(([token, color, name]) => (
            <div key={token} className="ks__swatch">
              <div
                className="ks__swatch-dot"
                style={{ background: color }}
                title={token}
              />
              <span>{name}</span>
            </div>
          ))}
        </div>
      </section>

      {/* Buttons */}
      <section className="ks__section">
        <p className="ks__section-title">Buttons</p>
        <div className="ks__stack">
          <div className="ks__row">
            <Button variant="primary">Primary</Button>
            <Button variant="secondary">Secondary</Button>
            <Button variant="ghost">Ghost</Button>
            <Button variant="danger">Danger</Button>
          </div>
          <div className="ks__row">
            <Button size="sm">Small</Button>
            <Button size="md">Medium</Button>
            <Button size="lg">Large</Button>
          </div>
          <div className="ks__row">
            <Button loading>Loading</Button>
            <Button disabled>Disabled</Button>
            <Button iconLeft={<Broom size={16} weight="duotone" />}>With icon</Button>
          </div>
        </div>
      </section>

      {/* Badges */}
      <section className="ks__section">
        <p className="ks__section-title">Badges</p>
        <div className="ks__row">
          <Badge variant="success" dot>Swept</Badge>
          <Badge variant="warning" dot>Pending</Badge>
          <Badge variant="danger"  dot>Failed</Badge>
          <Badge variant="info"    dot>Dry Run</Badge>
          <Badge variant="neutral">Ignored</Badge>
          <Badge variant="accent">Approved</Badge>
          <Badge variant="success" size="sm">sm</Badge>
        </div>
      </section>

      {/* Status Dots */}
      <section className="ks__section">
        <p className="ks__section-title">Status Dots</p>
        <div className="ks__row">
          <StatusDot status="connected"    label="Jellyfin" />
          <div className="ks__divider" />
          <StatusDot status="disconnected" label="Radarr" />
          <div className="ks__divider" />
          <StatusDot status="pending"      label="Sonarr" />
          <div className="ks__divider" />
          <StatusDot status="connected"    size="sm" label="sm" />
          <StatusDot status="connected"    size="lg" label="lg" />
        </div>
      </section>

      {/* Spinners */}
      <section className="ks__section">
        <p className="ks__section-title">Spinners</p>
        <div className="ks__row">
          <Spinner size="sm" />
          <Spinner size="md" />
          <Spinner size="lg" />
          <Spinner size="xl" />
          <Spinner size="md" color="muted" />
          <Button variant="primary" loading={loading} onClick={triggerLoad}>
            {loading ? 'Loading…' : 'Trigger load'}
          </Button>
        </div>
      </section>

      {/* Inputs */}
      <section className="ks__section">
        <p className="ks__section-title">Inputs</p>
        <div className="ks__form-grid">
          <Input
            label="Server URL"
            placeholder="https://jellyfin.local:8096"
            value={inputVal}
            onChange={e => setInputVal(e.target.value)}
            helper="Base URL of your Jellyfin server"
          />
          <Input
            label="API Key"
            type="password"
            placeholder="Enter API key"
          />
          <Input
            label="Max items"
            type="number"
            defaultValue={20}
            iconLeft={<MagnifyingGlass size={16} />}
          />
          <Input
            label="Instance name"
            defaultValue="My Library"
            error="Name is already taken"
          />
          <Input
            label="Disabled field"
            defaultValue="read-only"
            disabled
          />
        </div>
      </section>

      {/* Toggles */}
      <section className="ks__section">
        <p className="ks__section-title">Toggles</p>
        <div className="ks__stack">
          <Toggle
            checked={dryRun}
            onChange={setDryRun}
            label="Global dry-run mode"
            description="Simulate only — nothing will be deleted"
          />
          <Toggle
            checked={false}
            onChange={() => {}}
            label="Import exclusion"
            description="Prevent re-downloads after sweep"
          />
          <Toggle
            checked={true}
            onChange={() => {}}
            label="Disabled toggle"
            disabled
          />
        </div>
      </section>

      {/* Cards */}
      <section className="ks__section">
        <p className="ks__section-title">Cards</p>
        <div className="ks__card-grid">
          <Card variant="default">
            <CardHeader title="Default (glass)" actions={<Badge variant="accent">Live</Badge>} />
            <CardBody>
              <p style={{ color: 'var(--text-secondary)', fontSize: 'var(--text-sm)' }}>
                Frosted glass surface with backdrop blur.
              </p>
            </CardBody>
            <CardFooter>
              <Button variant="ghost" size="sm">Cancel</Button>
              <Button size="sm">Save</Button>
            </CardFooter>
          </Card>

          <Card variant="elevated">
            <CardHeader title="Elevated" />
            <CardBody>
              <div className="ks__row">
                <HardDrives size={24} weight="duotone" color="var(--accent)" />
                <span style={{ color: 'var(--text-secondary)', fontSize: 'var(--text-sm)' }}>
                  Solid surface, higher elevation shadow.
                </span>
              </div>
            </CardBody>
          </Card>

          <Card variant="flat">
            <CardHeader title="Flat" />
            <CardBody>
              <p style={{ color: 'var(--text-secondary)', fontSize: 'var(--text-sm)' }}>
                Surface-level background, minimal depth.
              </p>
            </CardBody>
          </Card>
        </div>
      </section>

      {/* Table */}
      <section className="ks__section">
        <p className="ks__section-title">Table</p>
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>Title</TableHeaderCell>
              <TableHeaderCell>Type</TableHeaderCell>
              <TableHeaderCell>Size</TableHeaderCell>
              <TableHeaderCell>Why flagged</TableHeaderCell>
              <TableHeaderCell>Status</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {DEMO_ROWS.map(row => (
              <TableRow key={row.title}>
                <TableCell>{row.title}</TableCell>
                <TableCell><Badge variant="info" size="sm">{row.type}</Badge></TableCell>
                <TableCell style={{ color: 'var(--text-secondary)' }}>{row.size}</TableCell>
                <TableCell style={{ color: 'var(--text-secondary)' }}>{row.why}</TableCell>
                <TableCell>
                  <Badge variant={STATUS_BADGE[row.status]} dot>{row.status}</Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>

        <div style={{ marginTop: 'var(--space-4)' }}>
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>Title</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              <TableEmpty>No sweep items in queue — create your first rule to get started.</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </section>

      {/* Modal */}
      <section className="ks__section">
        <p className="ks__section-title">Modal</p>
        <div className="ks__row">
          <Button variant="secondary" onClick={() => setModalOpen(true)}>
            Open modal
          </Button>
        </div>
        <Modal
          open={modalOpen}
          onClose={() => setModalOpen(false)}
          title="Confirm sweep"
          size="md"
          footer={
            <>
              <Button variant="ghost" onClick={() => setModalOpen(false)}>Cancel</Button>
              <Button variant="danger" onClick={() => setModalOpen(false)}>
                Delete 14 items
              </Button>
            </>
          }
        >
          <p style={{ color: 'var(--text-secondary)', marginBottom: 'var(--space-4)' }}>
            This will permanently delete 14 items (128 GB) and unmonitor them in Radarr/Sonarr.
            This action cannot be undone.
          </p>
          <div className="ks__row">
            <Badge variant="danger" dot>128 GB</Badge>
            <Badge variant="warning" dot>14 items</Badge>
          </div>
        </Modal>
      </section>

      {/* Toast */}
      <section className="ks__section">
        <p className="ks__section-title">Toast Notifications</p>
        <div className="ks__row">
          <Button
            variant="secondary"
            size="sm"
            iconLeft={<Lightning size={15} weight="fill" />}
            onClick={() => toast({ type: 'success', title: 'Sweep complete', message: 'Recovered 128 GB across 14 items.' })}
          >
            Success
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => toast({ type: 'warning', title: 'Failsafe triggered', message: 'Run halted at 20 items — limit reached.' })}
          >
            Warning
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => toast({ type: 'error', title: 'Connection failed', message: 'Could not reach Radarr at http://radarr:7878.' })}
          >
            Error
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => toast({ type: 'info', title: 'Dry-run mode active', message: 'No files will be deleted.' })}
          >
            Info
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => toast({ type: 'success', title: 'Persistent', duration: 0 })}
          >
            No auto-dismiss
          </Button>
        </div>
      </section>

      {/* Icon reference */}
      <section className="ks__section">
        <p className="ks__section-title">Phosphor Icons (duotone, used in app)</p>
        <div className="ks__row">
          {[
            [<Broom size={24} weight="duotone" />,         'Broom'],
            [<HardDrives size={24} weight="duotone" />,    'HardDrives'],
            [<Lightning size={24} weight="duotone" />,     'Lightning'],
            [<PlugsConnected size={24} weight="duotone" />, 'PlugsConnected'],
            [<Gear size={24} weight="duotone" />,          'Gear'],
          ].map(([icon, name]) => (
            <div key={String(name)} className="ks__swatch">
              <span style={{ color: 'var(--accent)' }}>{icon as React.ReactNode}</span>
              <span>{name as string}</span>
            </div>
          ))}
        </div>
      </section>
    </div>
  )
}
