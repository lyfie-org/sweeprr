import { useCallback, useEffect, useState } from 'react'
import { Plus, Trash, PencilSimple, PaperPlaneTilt, CheckCircle, XCircle } from '@phosphor-icons/react'
import {
  notificationsApi,
  NOTIFICATION_PROVIDER_TYPES,
  type NotificationSettingResponse,
  type NotificationProviderType,
  type CreateNotificationSettingRequest,
  type UpdateNotificationSettingRequest,
} from '../../api/notifications'
import { ApiError } from '../../api/client'
import {
  Badge,
  Button,
  Card,
  CardBody,
  Input,
  Modal,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeaderCell,
  TableRow,
  Toggle,
  useToast,
} from '../ui'
import './NotificationsSection.css'

const PROVIDER_LABELS: Record<NotificationProviderType, string> = {
  Discord: 'Discord',
  GenericWebhook: 'Generic Webhook',
}

const TRIGGER_FIELDS = [
  { key: 'triggerOnSweepComplete', label: 'Sweep Complete' },
  { key: 'triggerOnFailsafe', label: 'Failsafe Tripped' },
  { key: 'triggerOnPendingItems', label: 'Pending Items' },
  { key: 'triggerOnConnectionError', label: 'Connection Error' },
] as const

export function NotificationsSection() {
  const { toast } = useToast()
  const [settings, setSettings] = useState<NotificationSettingResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<NotificationSettingResponse | null>(null)

  const fetchSettings = useCallback(async () => {
    try {
      const data = await notificationsApi.getAll()
      setSettings(data)
    } catch {
      toast({ type: 'error', title: 'Failed to load notification settings' })
    } finally {
      setLoading(false)
    }
  }, [toast])

  useEffect(() => {
    fetchSettings()
  }, [fetchSettings])

  const handleDelete = async (id: number) => {
    try {
      await notificationsApi.delete(id)
      setSettings(prev => prev.filter(s => s.id !== id))
      toast({ type: 'success', title: 'Webhook removed' })
    } catch {
      toast({ type: 'error', title: 'Failed to remove webhook' })
    }
  }

  const handleToggleEnabled = async (setting: NotificationSettingResponse) => {
    try {
      const updated = await notificationsApi.update(setting.id, { isEnabled: !setting.isEnabled })
      setSettings(prev => prev.map(s => (s.id === updated.id ? updated : s)))
    } catch {
      toast({ type: 'error', title: 'Failed to update webhook' })
    }
  }

  const openCreate = () => {
    setEditing(null)
    setShowForm(true)
  }

  const openEdit = (setting: NotificationSettingResponse) => {
    setEditing(setting)
    setShowForm(true)
  }

  const handleSaved = (saved: NotificationSettingResponse) => {
    setShowForm(false)
    setSettings(prev => {
      const exists = prev.some(s => s.id === saved.id)
      return exists ? prev.map(s => (s.id === saved.id ? saved : s)) : [...prev, saved]
    })
  }

  return (
    <Card>
      <CardBody>
        <div className="notifications__header">
          <p className="settings-section__label" style={{ margin: 0 }}>Notifications</p>
          <Button
            variant="secondary"
            size="sm"
            iconLeft={<Plus size={14} weight="bold" />}
            onClick={openCreate}
          >
            Add Webhook
          </Button>
        </div>
        <p className="notifications__hint">
          Send Discord embeds or generic JSON webhooks when a sweep completes, a failsafe trips,
          new items enter the queue, or the Jellyfin connection drops.
        </p>

        {loading ? (
          <div style={{ display: 'flex', justifyContent: 'center', padding: 'var(--space-6)' }}>
            <Spinner size="md" />
          </div>
        ) : (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Provider</TableHeaderCell>
                <TableHeaderCell>Triggers</TableHeaderCell>
                <TableHeaderCell>Enabled</TableHeaderCell>
                <TableHeaderCell>{null}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {settings.length === 0 ? (
                <TableEmpty>No notification webhooks configured.</TableEmpty>
              ) : (
                settings.map(s => (
                  <TableRow key={s.id}>
                    <TableCell>
                      <div className="notifications__name">{s.name}</div>
                      <div className="notifications__masked">{s.maskedWebhookUrl}</div>
                    </TableCell>
                    <TableCell>
                      <Badge variant="info" size="sm">{PROVIDER_LABELS[s.providerType] ?? s.providerType}</Badge>
                    </TableCell>
                    <TableCell>
                      <div className="notifications__triggers">
                        {TRIGGER_FIELDS.filter(t => s[t.key]).map(t => (
                          <Badge key={t.key} variant="neutral" size="sm">{t.label}</Badge>
                        ))}
                        {TRIGGER_FIELDS.every(t => !s[t.key]) && (
                          <span className="notifications__no-triggers">None</span>
                        )}
                      </div>
                    </TableCell>
                    <TableCell>
                      <Toggle checked={s.isEnabled} onChange={() => handleToggleEnabled(s)} />
                    </TableCell>
                    <TableCell>
                      <div className="notifications__actions">
                        <Button
                          variant="ghost"
                          size="sm"
                          aria-label={`Edit ${s.name}`}
                          onClick={() => openEdit(s)}
                        >
                          <PencilSimple size={16} weight="duotone" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          aria-label={`Delete ${s.name}`}
                          onClick={() => handleDelete(s.id)}
                        >
                          <Trash size={16} weight="duotone" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        )}
      </CardBody>

      {showForm && (
        <NotificationFormModal
          initial={editing}
          onClose={() => setShowForm(false)}
          onSaved={handleSaved}
        />
      )}
    </Card>
  )
}

// ── Create / edit modal ──────────────────────────────────────────────────────

interface NotificationFormModalProps {
  initial: NotificationSettingResponse | null
  onClose: () => void
  onSaved: (saved: NotificationSettingResponse) => void
}

function NotificationFormModal({ initial, onClose, onSaved }: NotificationFormModalProps) {
  const { toast } = useToast()
  const isEdit = initial !== null

  const [name, setName] = useState(initial?.name ?? '')
  const [providerType, setProviderType] = useState<NotificationProviderType>(initial?.providerType ?? 'Discord')
  const [webhookUrl, setWebhookUrl] = useState('')
  const [isEnabled, setIsEnabled] = useState(initial?.isEnabled ?? true)
  const [triggerOnFailsafe, setTriggerOnFailsafe] = useState(initial?.triggerOnFailsafe ?? true)
  const [triggerOnSweepComplete, setTriggerOnSweepComplete] = useState(initial?.triggerOnSweepComplete ?? true)
  const [triggerOnPendingItems, setTriggerOnPendingItems] = useState(initial?.triggerOnPendingItems ?? false)
  const [triggerOnConnectionError, setTriggerOnConnectionError] = useState(initial?.triggerOnConnectionError ?? false)
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null)

  const handleTest = async () => {
    setTesting(true)
    setTestResult(null)
    try {
      const result = webhookUrl.trim()
        ? await notificationsApi.test({ providerType, webhookUrl: webhookUrl.trim() })
        : isEdit
          ? await notificationsApi.testExisting(initial.id)
          : null

      if (result === null) {
        toast({ type: 'error', title: 'Enter a webhook URL to test' })
        return
      }

      setTestResult({
        success: result.success,
        message: result.success ? 'Test notification sent!' : (result.error ?? 'Webhook test failed'),
      })
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Test failed'
      setTestResult({ success: false, message: msg })
    } finally {
      setTesting(false)
    }
  }

  const handleSave = async () => {
    if (!name.trim()) return
    if (!isEdit && !webhookUrl.trim()) return

    setSaving(true)
    try {
      let saved: NotificationSettingResponse
      if (isEdit) {
        const req: UpdateNotificationSettingRequest = {
          name: name.trim(),
          isEnabled,
          triggerOnFailsafe,
          triggerOnSweepComplete,
          triggerOnPendingItems,
          triggerOnConnectionError,
        }
        if (webhookUrl.trim()) req.webhookUrl = webhookUrl.trim()
        saved = await notificationsApi.update(initial.id, req)
      } else {
        const req: CreateNotificationSettingRequest = {
          name: name.trim(),
          providerType,
          webhookUrl: webhookUrl.trim(),
          isEnabled,
          triggerOnFailsafe,
          triggerOnSweepComplete,
          triggerOnPendingItems,
          triggerOnConnectionError,
        }
        saved = await notificationsApi.create(req)
      }
      onSaved(saved)
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Failed to save webhook'
      toast({ type: 'error', title: 'Save failed', message: msg })
    } finally {
      setSaving(false)
    }
  }

  const canSave = name.trim().length > 0 && (isEdit || webhookUrl.trim().length > 0) && !saving
  const canTest = (webhookUrl.trim().length > 0 || isEdit) && !testing

  const footer = (
    <>
      <Button variant="ghost" onClick={onClose} disabled={saving}>Cancel</Button>
      <Button variant="primary" disabled={!canSave} onClick={handleSave}>
        {saving ? <Spinner size="sm" /> : null}
        {isEdit ? 'Save Changes' : 'Add Webhook'}
      </Button>
    </>
  )

  return (
    <Modal open title={isEdit ? `Edit ${initial.name}` : 'Add Webhook'} onClose={onClose} footer={footer}>
      <div className="notifications__form">
        <Input
          label="Name"
          value={name}
          onChange={e => setName(e.target.value)}
          placeholder="e.g. Discord alerts"
          disabled={saving}
        />

        {isEdit ? (
          <div>
            <label className="notifications__field-label">Provider</label>
            <div className="notifications__provider-readonly">
              <Badge variant="info" size="sm">{PROVIDER_LABELS[initial.providerType]}</Badge>
              <span className="notifications__provider-note">Provider type cannot be changed after creation.</span>
            </div>
          </div>
        ) : (
          <div>
            <label className="notifications__field-label" htmlFor="notification-provider">Provider</label>
            <select
              id="notification-provider"
              className="notifications__select"
              value={providerType}
              onChange={e => setProviderType(e.target.value as NotificationProviderType)}
              disabled={saving}
            >
              {NOTIFICATION_PROVIDER_TYPES.map(p => (
                <option key={p} value={p}>{PROVIDER_LABELS[p]}</option>
              ))}
            </select>
          </div>
        )}

        <Input
          label="Webhook URL"
          type="password"
          value={webhookUrl}
          onChange={e => setWebhookUrl(e.target.value)}
          placeholder={isEdit ? `Current: ${initial.maskedWebhookUrl}` : 'https://discord.com/api/webhooks/...'}
          disabled={saving}
          helper={isEdit ? 'Leave blank to keep the existing webhook URL' : undefined}
        />

        <Toggle
          checked={isEnabled}
          onChange={setIsEnabled}
          label="Enabled"
          description="Disabled webhooks are kept but never receive notifications."
          disabled={saving}
        />

        <div>
          <label className="notifications__field-label">Send notifications on</label>
          <div className="notifications__trigger-list">
            <Toggle
              checked={triggerOnSweepComplete}
              onChange={setTriggerOnSweepComplete}
              label="Sweep Complete"
              description="A scheduled or manual sweep run finishes."
              disabled={saving}
            />
            <Toggle
              checked={triggerOnFailsafe}
              onChange={setTriggerOnFailsafe}
              label="Failsafe Tripped"
              description="A safety limit blocked some or all of a sweep run."
              disabled={saving}
            />
            <Toggle
              checked={triggerOnPendingItems}
              onChange={setTriggerOnPendingItems}
              label="Pending Items"
              description="New items are added to the Sweep Queue awaiting review."
              disabled={saving}
            />
            <Toggle
              checked={triggerOnConnectionError}
              onChange={setTriggerOnConnectionError}
              label="Connection Error"
              description="Sweeprr loses its connection to Jellyfin after repeated retries."
              disabled={saving}
            />
          </div>
        </div>

        <div className="notifications__test-row">
          <Button
            variant="secondary"
            size="sm"
            iconLeft={testing ? <Spinner size="sm" /> : <PaperPlaneTilt size={14} weight="bold" />}
            onClick={handleTest}
            disabled={!canTest}
          >
            Send Test
          </Button>
          {testResult && (
            <span className={`notifications__test-result ${testResult.success ? 'notifications__test-result--ok' : 'notifications__test-result--fail'}`}>
              {testResult.success ? <CheckCircle size={15} weight="fill" /> : <XCircle size={15} weight="fill" />}
              {testResult.message}
            </span>
          )}
        </div>
      </div>
    </Modal>
  )
}
