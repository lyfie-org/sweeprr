import { useCallback, useEffect, useRef, useState } from 'react'
import {
  Plus,
  Television,
  FilmSlate,
  VideoCamera,
  PlugsConnected,
  CheckCircle,
  XCircle,
  PencilSimple,
  Trash,
  Key,
  Link,
} from '@phosphor-icons/react'
import {
  connectionsApi,
  CONNECTION_TYPE_LABELS,
  type ConnectionResponse,
  type ConnectionType,
  type ConnectionTestResult,
} from '../api/connections'
import { ApiError } from '../api/client'
import {
  Button,
  Card,
  CardBody,
  CardFooter,
  Modal,
  Input,
  Toggle,
  Badge,
  StatusDot,
  Spinner,
  useToast,
} from '../components/ui'
import './ConnectionsPage.css'

// ── Helpers ───────────────────────────────────────────────────────────────────

const TYPE_ICONS: Record<ConnectionType, React.ReactNode> = {
  0: <Television size={20} weight="duotone" />,
  1: <FilmSlate size={20} weight="duotone" />,
  2: <VideoCamera size={20} weight="duotone" />,
}

const TYPE_ICON_CLASS: Record<ConnectionType, string> = {
  0: 'conn-card__icon--jellyfin',
  1: 'conn-card__icon--radarr',
  2: 'conn-card__icon--sonarr',
}

function statusFromConn(conn: ConnectionResponse): 'connected' | 'disconnected' | 'pending' {
  if (!conn.isEnabled) return 'pending'
  if (conn.lastConnectionOk === true) return 'connected'
  if (conn.lastConnectionOk === false) return 'disconnected'
  return 'pending'
}

function relativeTime(iso: string | null): string {
  if (!iso) return 'Never tested'
  const diff = Date.now() - new Date(iso).getTime()
  const m = Math.floor(diff / 60_000)
  if (m < 1) return 'Just now'
  if (m < 60) return `${m}m ago`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h}h ago`
  return `${Math.floor(h / 24)}d ago`
}

// ── Modal form state ──────────────────────────────────────────────────────────

interface FormState {
  name: string
  type: ConnectionType
  baseUrl: string
  apiKey: string
  allowInsecure: boolean
  isEnabled: boolean
}

function emptyForm(): FormState {
  return { name: '', type: 0, baseUrl: '', apiKey: '', allowInsecure: false, isEnabled: true }
}

function formFromConn(c: ConnectionResponse): FormState {
  return {
    name: c.name,
    type: c.type,
    baseUrl: c.baseUrl,
    apiKey: '',           // empty means "keep existing key"
    allowInsecure: c.allowInsecure,
    isEnabled: c.isEnabled,
  }
}

// ── Connection Modal ──────────────────────────────────────────────────────────

interface ConnModalProps {
  editing: ConnectionResponse | null
  onClose: () => void
  onSaved: () => void
}

function ConnModal({ editing, onClose, onSaved }: ConnModalProps) {
  const { toast } = useToast()
  const [form, setForm] = useState<FormState>(editing ? formFromConn(editing) : emptyForm())
  const [errors, setErrors] = useState<Partial<Record<keyof FormState, string>>>({})
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<ConnectionTestResult | null>(null)

  const isEditing = editing !== null

  function setField<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm(prev => ({ ...prev, [key]: value }))
    setErrors(prev => ({ ...prev, [key]: undefined }))
    setTestResult(null)
  }

  function validate(): boolean {
    const next: Partial<Record<keyof FormState, string>> = {}
    if (!form.name.trim()) next.name = 'Name is required'
    if (!form.baseUrl.trim()) {
      next.baseUrl = 'Base URL is required'
    } else if (!/^https?:\/\/.+/.test(form.baseUrl.trim())) {
      next.baseUrl = 'Must be a valid http:// or https:// URL'
    }
    if (!isEditing && !form.apiKey.trim()) {
      next.apiKey = 'API key is required for new connections'
    }
    setErrors(next)
    return Object.keys(next).length === 0
  }

  async function handleTest() {
    if (!validate()) return
    setTesting(true)
    setTestResult(null)
    try {
      let result: ConnectionTestResult
      if (isEditing && !form.apiKey.trim()) {
        result = await connectionsApi.testSaved(editing.id)
      } else {
        result = await connectionsApi.testUnsaved(
          form.type,
          form.baseUrl.trim(),
          form.apiKey.trim(),
          form.allowInsecure,
        )
      }
      setTestResult(result)
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Test request failed'
      setTestResult({ success: false, serverName: null, version: null, latencyMs: null, errorMessage: msg })
    } finally {
      setTesting(false)
    }
  }

  async function handleSave() {
    if (!validate()) return
    setSaving(true)
    try {
      const req = {
        name: form.name.trim(),
        type: form.type,
        baseUrl: form.baseUrl.trim(),
        // null = keep existing key; non-empty string = new key
        apiKey: isEditing ? (form.apiKey.trim() || null) : form.apiKey.trim(),
        isEnabled: form.isEnabled,
        allowInsecure: form.allowInsecure,
      }

      if (isEditing) {
        await connectionsApi.update(editing.id, req)
        toast({ type: 'success', title: 'Connection updated' })
      } else {
        const { warning } = await connectionsApi.create(req)
        toast({ type: 'success', title: 'Connection added' })
        if (warning) toast({ type: 'warning', title: 'Note', message: warning, duration: 6000 })
      }
      onSaved()
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Failed to save connection'
      toast({ type: 'error', title: 'Save failed', message: msg })
    } finally {
      setSaving(false)
    }
  }

  const footer = (
    <div style={{ display: 'flex', gap: 'var(--space-2)', justifyContent: 'flex-end', width: '100%' }}>
      <Button variant="ghost" onClick={onClose} disabled={saving || testing}>
        Cancel
      </Button>
      <Button variant="secondary" size="md" onClick={handleTest} loading={testing} disabled={saving}>
        Test First
      </Button>
      <Button variant="primary" size="md" onClick={handleSave} loading={saving} disabled={testing}>
        {isEditing ? 'Save Changes' : 'Add Connection'}
      </Button>
    </div>
  )

  return (
    <Modal
      open
      onClose={onClose}
      title={isEditing ? 'Edit Connection' : 'Add Connection'}
      size="md"
      footer={footer}
    >
      <div className="conn-modal__form">
        <Input
          label="Name"
          value={form.name}
          onChange={e => setField('name', e.target.value)}
          placeholder="My Jellyfin"
          error={errors.name}
          required
          disabled={saving}
        />

        <div>
          <span className="conn-modal__select-label">Service Type</span>
          <div className="conn-modal__select-wrap">
            <select
              className="conn-modal__select"
              value={form.type}
              onChange={e => setField('type', parseInt(e.target.value, 10) as ConnectionType)}
              disabled={saving || isEditing}
            >
              <option value={0}>Jellyfin</option>
              <option value={1}>Radarr</option>
              <option value={2}>Sonarr</option>
            </select>
          </div>
          {isEditing && (
            <p className="conn-modal__hint">Service type cannot be changed after creation</p>
          )}
        </div>

        <Input
          label="Base URL"
          value={form.baseUrl}
          onChange={e => setField('baseUrl', e.target.value)}
          placeholder="http://192.168.1.100:8096"
          error={errors.baseUrl}
          required
          disabled={saving}
          iconLeft={<Link size={14} />}
        />

        <Input
          label="API Key"
          type="password"
          value={form.apiKey}
          onChange={e => setField('apiKey', e.target.value)}
          placeholder={
            isEditing && editing?.hasKey
              ? 'Leave blank to keep existing key'
              : 'Paste your API key'
          }
          error={errors.apiKey}
          required={!isEditing}
          disabled={saving}
          iconLeft={<Key size={14} />}
        />

        <div className="conn-modal__toggles">
          <Toggle
            checked={form.allowInsecure}
            onChange={v => setField('allowInsecure', v)}
            label="Allow self-signed TLS"
            description="Required if your service uses a self-signed certificate"
            disabled={saving}
          />
          <Toggle
            checked={form.isEnabled}
            onChange={v => setField('isEnabled', v)}
            label="Enable connection"
            disabled={saving}
          />
        </div>

        {testResult && !testing && (
          <div
            className={`conn-modal__test-result conn-modal__test-result--${testResult.success ? 'success' : 'error'}`}
          >
            <span className="conn-modal__test-result-icon">
              {testResult.success
                ? <CheckCircle size={16} weight="fill" />
                : <XCircle size={16} weight="fill" />}
            </span>
            <span>
              {testResult.success
                ? [
                    testResult.serverName ?? 'Connected',
                    testResult.version && `v${testResult.version}`,
                    testResult.latencyMs != null && `${testResult.latencyMs}ms`,
                  ].filter(Boolean).join('  ·  ')
                : (testResult.errorMessage ?? 'Connection failed')}
            </span>
          </div>
        )}
      </div>
    </Modal>
  )
}

// ── Connection Card ───────────────────────────────────────────────────────────

interface ConnCardProps {
  conn: ConnectionResponse
  onEdit: () => void
  onRefresh: () => void
}

function ConnCard({ conn, onEdit, onRefresh }: ConnCardProps) {
  const { toast } = useToast()
  const [testResult, setTestResult] = useState<ConnectionTestResult | null>(null)
  const [testing, setTesting] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const confirmTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  async function handleTest() {
    setTesting(true)
    setTestResult(null)
    try {
      const result = await connectionsApi.testSaved(conn.id)
      setTestResult(result)
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Test request failed'
      setTestResult({ success: false, serverName: null, version: null, latencyMs: null, errorMessage: msg })
    } finally {
      setTesting(false)
    }
  }

  function handleDeleteClick() {
    if (!confirmDelete) {
      setConfirmDelete(true)
      confirmTimer.current = setTimeout(() => setConfirmDelete(false), 3000)
      return
    }
    if (confirmTimer.current) clearTimeout(confirmTimer.current)
    doDelete()
  }

  async function doDelete() {
    setDeleting(true)
    try {
      await connectionsApi.delete(conn.id)
      toast({ type: 'success', title: 'Connection removed' })
      onRefresh()
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Delete failed'
      toast({ type: 'error', title: 'Delete failed', message: msg })
      setDeleting(false)
      setConfirmDelete(false)
    }
  }

  return (
    <Card>
      <CardBody>
        <div className="conn-card__header">
          <div className={`conn-card__icon ${TYPE_ICON_CLASS[conn.type]}`}>
            {TYPE_ICONS[conn.type]}
          </div>
          <div className="conn-card__meta">
            <h3 className="conn-card__name" title={conn.name}>{conn.name}</h3>
            <div className="conn-card__badges">
              <StatusDot status={statusFromConn(conn)} size="sm" />
              <Badge variant="neutral" size="sm">{CONNECTION_TYPE_LABELS[conn.type]}</Badge>
              {!conn.isEnabled && <Badge variant="warning" size="sm">Disabled</Badge>}
              {conn.allowInsecure && <Badge variant="info" size="sm">TLS: self-signed</Badge>}
            </div>
          </div>
        </div>

        <div className="conn-card__url" title={conn.baseUrl}>{conn.baseUrl}</div>

        {conn.hasKey && (
          <div className="conn-card__key-row">
            <Key size={11} />
            <span>{conn.maskedKey}</span>
          </div>
        )}

        <div className="conn-card__last-tested">
          Last tested: {relativeTime(conn.lastConnectedAt)}
          {conn.lastConnectionOk === false && (
            <span style={{ color: 'var(--danger)', marginLeft: 'var(--space-2)' }}>— failed</span>
          )}
        </div>

        {testing && (
          <div className="conn-card__test-chip conn-card__test-chip--testing">
            <Spinner size="sm" color="muted" />
            Testing connection…
          </div>
        )}

        {testResult && !testing && (
          <div
            className={`conn-card__test-chip conn-card__test-chip--${testResult.success ? 'success' : 'error'}`}
          >
            {testResult.success ? (
              <>
                <CheckCircle size={12} weight="fill" />
                <span>
                  {[
                    testResult.serverName ?? 'Connected',
                    testResult.version && `v${testResult.version}`,
                    testResult.latencyMs != null && `${testResult.latencyMs}ms`,
                  ].filter(Boolean).join('  ·  ')}
                </span>
              </>
            ) : (
              <>
                <XCircle size={12} weight="fill" />
                <span>{testResult.errorMessage}</span>
              </>
            )}
          </div>
        )}
      </CardBody>

      <CardFooter>
        <div className="conn-card__actions">
          <Button
            variant="secondary"
            size="sm"
            onClick={handleTest}
            loading={testing}
            disabled={deleting}
          >
            Test
          </Button>
          <Button
            variant="ghost"
            size="sm"
            iconLeft={<PencilSimple size={14} />}
            onClick={onEdit}
            disabled={testing || deleting}
          >
            Edit
          </Button>
          <Button
            variant={confirmDelete ? 'danger' : 'ghost'}
            size="sm"
            iconLeft={!confirmDelete ? <Trash size={14} /> : undefined}
            onClick={handleDeleteClick}
            loading={deleting}
            disabled={testing}
          >
            {confirmDelete ? 'Confirm delete?' : 'Delete'}
          </Button>
        </div>
      </CardFooter>
    </Card>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function ConnectionsPage() {
  const { toast } = useToast()
  const [connections, setConnections] = useState<ConnectionResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingConn, setEditingConn] = useState<ConnectionResponse | null>(null)

  const fetchConnections = useCallback(async () => {
    try {
      const data = await connectionsApi.getAll()
      setConnections(data)
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Failed to load connections'
      toast({ type: 'error', title: 'Load failed', message: msg })
    } finally {
      setLoading(false)
    }
  }, [toast])

  useEffect(() => {
    fetchConnections()
  }, [fetchConnections])

  function openAdd() {
    setEditingConn(null)
    setModalOpen(true)
  }

  function openEdit(conn: ConnectionResponse) {
    setEditingConn(conn)
    setModalOpen(true)
  }

  function handleSaved() {
    setModalOpen(false)
    fetchConnections()
  }

  if (loading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', padding: 'var(--space-16)' }}>
        <Spinner size="lg" />
      </div>
    )
  }

  return (
    <div>
      <div className="connections-page__header">
        <h1 className="connections-page__title">Connections</h1>
        <Button
          variant="primary"
          size="sm"
          iconLeft={<Plus size={16} weight="bold" />}
          onClick={openAdd}
        >
          Add Connection
        </Button>
      </div>

      {connections.length === 0 ? (
        <Card>
          <CardBody>
            <div className="connections-empty">
              <div className="connections-empty__icon">
                <PlugsConnected size={52} weight="duotone" />
              </div>
              <h3 className="connections-empty__title">No connections yet</h3>
              <p className="connections-empty__body">
                Add your Jellyfin, Radarr, or Sonarr instance to get started. Sweeprr needs at
                least one Jellyfin connection to scan your library.
              </p>
            </div>
          </CardBody>
        </Card>
      ) : (
        <div className="connections-grid">
          {connections.map(conn => (
            <ConnCard
              key={conn.id}
              conn={conn}
              onEdit={() => openEdit(conn)}
              onRefresh={fetchConnections}
            />
          ))}
        </div>
      )}

      {modalOpen && (
        <ConnModal
          key={editingConn?.id ?? 'new'}
          editing={editingConn}
          onClose={() => setModalOpen(false)}
          onSaved={handleSaved}
        />
      )}
    </div>
  )
}
