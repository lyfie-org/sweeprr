import { useCallback, useEffect, useState } from 'react'
import {
  Warning,
  CheckCircle,
  Sun,
  Moon,
} from '@phosphor-icons/react'
import { settingsApi, type SettingsDto, type UpdateSettingsRequest } from '../api/settings'
import { systemApi } from '../api/system'
import { ApiError } from '../api/client'
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Input,
  Toggle,
  Spinner,
  useToast,
} from '../components/ui'
import './SettingsPage.css'

// ── Cron description ──────────────────────────────────────────────────────────

function describeCron(expr: string): { label: string; valid: boolean } | null {
  const parts = expr.trim().split(/\s+/)
  if (parts.length !== 5) return null
  const [min, hour, dom, month, dow] = parts

  if (min === '0' && hour === '*' && dom === '*' && month === '*' && dow === '*') {
    return { label: 'Every hour', valid: true }
  }
  if (dom === '*' && month === '*' && dow === '*' && !/[*,/-]/.test(min) && !/[*,/-]/.test(hour)) {
    const h = hour.padStart(2, '0')
    const m = min.padStart(2, '0')
    return { label: `Daily at ${h}:${m}`, valid: true }
  }
  if (dom === '*' && month === '*' && !/[*,/-]/.test(dow) && min === '0' && !/[*,/-]/.test(hour)) {
    const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
    const d = parseInt(dow, 10)
    const dayName = isNaN(d) ? dow : (days[d] ?? dow)
    return { label: `Weekly on ${dayName} at ${hour.padStart(2, '0')}:00`, valid: true }
  }

  // Basic structure looks okay — let the server validate
  return { label: 'Custom schedule (validated on save)', valid: true }
}

// ── Form state ────────────────────────────────────────────────────────────────

interface FormState {
  instanceName: string
  globalDryRun: boolean
  defaultCron: string
  maxItemsPerRun: string
  maxGbPerRun: string
  pessimisticSizeGb: string
  libCapEnabled: boolean
  libCapPct: string       // 0–100 (converted to 0–1 for the API)
  overBroadEnabled: boolean
  overBroadPct: string    // 0–100
}

function settingsToForm(s: SettingsDto): FormState {
  return {
    instanceName: s.instanceName,
    globalDryRun: s.globalDryRun,
    defaultCron: s.defaultCron,
    maxItemsPerRun: s.maxItemsPerRun.toString(),
    maxGbPerRun: s.maxGbPerRun.toString(),
    pessimisticSizeGb: s.pessimisticSizeGb.toString(),
    libCapEnabled: s.libraryPercentCap !== null,
    libCapPct: s.libraryPercentCap !== null
      ? (s.libraryPercentCap * 100).toFixed(1)
      : '20',
    overBroadEnabled: s.overBroadMatchPct !== null,
    overBroadPct: s.overBroadMatchPct !== null
      ? (s.overBroadMatchPct * 100).toFixed(1)
      : '30',
  }
}

// ── Theme helpers ─────────────────────────────────────────────────────────────

function getStoredTheme(): 'light' | 'dark' {
  return (localStorage.getItem('sweeprr_theme') as 'light' | 'dark') ?? 'dark'
}

function applyTheme(theme: 'light' | 'dark') {
  if (theme === 'light') {
    document.documentElement.setAttribute('data-theme', 'light')
  } else {
    document.documentElement.removeAttribute('data-theme')
  }
  localStorage.setItem('sweeprr_theme', theme)
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function SettingsPage() {
  const { toast } = useToast()
  const [settings, setSettings] = useState<SettingsDto | null>(null)
  const [form, setForm] = useState<FormState | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [savedFlash, setSavedFlash] = useState(false)
  const [cronError, setCronError] = useState<string | null>(null)
  const [theme, setTheme] = useState<'light' | 'dark'>(getStoredTheme)
  const [version, setVersion] = useState<string>('')

  useEffect(() => {
    systemApi.getInfo()
      .then(data => setVersion(data.version))
      .catch(() => {})
  }, [])

  const fetchSettings = useCallback(async () => {
    try {
      const data = await settingsApi.get()
      setSettings(data)
      setForm(settingsToForm(data))
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Failed to load settings'
      toast({ type: 'error', title: 'Load failed', message: msg })
    } finally {
      setLoading(false)
    }
  }, [toast])

  useEffect(() => {
    fetchSettings()
  }, [fetchSettings])

  function setF<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm(prev => prev ? { ...prev, [key]: value } : prev)
    if (key === 'defaultCron') setCronError(null)
  }

  function toggleTheme() {
    const next = theme === 'dark' ? 'light' : 'dark'
    setTheme(next)
    applyTheme(next)
  }

  async function handleSave() {
    if (!form || !settings) return
    setSaving(true)
    setCronError(null)

    const maxItems = parseInt(form.maxItemsPerRun, 10)
    const maxGb = parseFloat(form.maxGbPerRun)
    const pessGb = parseFloat(form.pessimisticSizeGb)

    if (isNaN(maxItems) || maxItems < 0) {
      toast({ type: 'error', title: 'Validation error', message: 'Max Items must be a non-negative integer' })
      setSaving(false)
      return
    }
    if (isNaN(maxGb) || maxGb < 0) {
      toast({ type: 'error', title: 'Validation error', message: 'Max GB must be a non-negative number' })
      setSaving(false)
      return
    }
    if (isNaN(pessGb) || pessGb < 0) {
      toast({ type: 'error', title: 'Validation error', message: 'Pessimistic Size must be a non-negative number' })
      setSaving(false)
      return
    }

    const req: UpdateSettingsRequest = {
      instanceName: form.instanceName.trim(),
      globalDryRun: form.globalDryRun,
      defaultCron: form.defaultCron.trim(),
      maxItemsPerRun: maxItems,
      maxGbPerRun: maxGb,
      pessimisticSizeGb: pessGb,
    }

    if (form.libCapEnabled) {
      const v = parseFloat(form.libCapPct)
      if (isNaN(v) || v <= 0 || v > 100) {
        toast({ type: 'error', title: 'Validation error', message: 'Library % cap must be between 0.1 and 100' })
        setSaving(false)
        return
      }
      req.libraryPercentCap = v / 100
    } else {
      req.clearLibraryPercentCap = true
    }

    if (form.overBroadEnabled) {
      const v = parseFloat(form.overBroadPct)
      if (isNaN(v) || v <= 0 || v > 100) {
        toast({ type: 'error', title: 'Validation error', message: 'Over-broad match % must be between 0.1 and 100' })
        setSaving(false)
        return
      }
      req.overBroadMatchPct = v / 100
    } else {
      req.clearOverBroadMatchPct = true
    }

    try {
      const updated = await settingsApi.patch(req)
      setSettings(updated)
      setForm(settingsToForm(updated))
      setSavedFlash(true)
      setTimeout(() => setSavedFlash(false), 2500)
      toast({ type: 'success', title: 'Settings saved' })
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.message.toLowerCase().includes('cron')) {
          setCronError(err.message)
        }
        toast({ type: 'error', title: 'Save failed', message: err.message })
      } else {
        toast({ type: 'error', title: 'Save failed' })
      }
    } finally {
      setSaving(false)
    }
  }

  if (loading || !form) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', padding: 'var(--space-16)' }}>
        <Spinner size="lg" />
      </div>
    )
  }

  const cronDesc = describeCron(form.defaultCron)

  return (
    <div>
      <div className="settings-page__header">
        <h1 className="settings-page__title">Settings</h1>
      </div>

      <div className="settings-sections">

        {/* ── Instance ── */}
        <Card>
          <CardBody>
            <p className="settings-section__label">Instance</p>
            <div className="settings-section__fields">
              <Input
                label="Instance name"
                value={form.instanceName}
                onChange={e => setF('instanceName', e.target.value)}
                placeholder="My Sweeprr"
                disabled={saving}
                helper="Shown in the header and notifications"
              />
            </div>
          </CardBody>
        </Card>

        {/* ── Schedule ── */}
        <Card>
          <CardBody>
            <p className="settings-section__label">Schedule</p>
            <div className="settings-section__fields">
              <div>
                <Input
                  label="Default scan schedule (cron)"
                  value={form.defaultCron}
                  onChange={e => setF('defaultCron', e.target.value)}
                  placeholder="0 3 * * *"
                  error={cronError ?? undefined}
                  disabled={saving}
                  helper="Applied to all rule groups without a custom schedule override"
                  style={{ fontFamily: 'var(--font-mono)' }}
                />
                {!cronError && cronDesc && (
                  <p className={`settings-cron__preview settings-cron__preview--valid`}>
                    ✓ {cronDesc.label}
                  </p>
                )}
              </div>
            </div>
          </CardBody>
        </Card>

        {/* ── Safety ── */}
        <Card className="settings-safety-card">
          <CardHeader>
            <div className="settings-safety-header">
              <Warning size={16} weight="fill" />
              Safety — Anti-Wipe Limits
            </div>
          </CardHeader>
          <CardBody>
            <div className="settings-section__fields">
              <div className="settings-row-2col">
                <Input
                  label="Max items per run"
                  type="number"
                  value={form.maxItemsPerRun}
                  onChange={e => setF('maxItemsPerRun', e.target.value)}
                  min={0}
                  disabled={saving}
                  helper="0 = block all runs"
                />
                <Input
                  label="Max GB per run"
                  type="number"
                  value={form.maxGbPerRun}
                  onChange={e => setF('maxGbPerRun', e.target.value)}
                  min={0}
                  step={0.5}
                  disabled={saving}
                  helper="0 = block all runs"
                />
              </div>

              <Input
                label="Pessimistic size (GB)"
                type="number"
                value={form.pessimisticSizeGb}
                onChange={e => setF('pessimisticSizeGb', e.target.value)}
                min={0}
                step={0.1}
                disabled={saving}
                helper="Assumed size for items with unknown file size — counted toward the GB cap"
              />

              {/* Library % cap */}
              <div>
                <div style={{ marginBottom: 'var(--space-2)' }}>
                  <label
                    style={{
                      fontSize: 'var(--text-sm)',
                      fontWeight: 'var(--font-weight-medium)',
                      color: 'var(--text-secondary)',
                    }}
                  >
                    Library % cap
                  </label>
                </div>
                <div className="cap-field__row">
                  <label className="cap-field__enable">
                    <input
                      type="checkbox"
                      checked={form.libCapEnabled}
                      onChange={e => setF('libCapEnabled', e.target.checked)}
                      disabled={saving}
                    />
                    Enable
                  </label>
                  <div className="cap-field__input">
                    <Input
                      type="number"
                      value={form.libCapPct}
                      onChange={e => setF('libCapPct', e.target.value)}
                      min={0.1}
                      max={100}
                      step={0.1}
                      disabled={saving || !form.libCapEnabled}
                      placeholder="20"
                    />
                  </div>
                  <span style={{ fontSize: 'var(--text-sm)', color: 'var(--text-muted)', flexShrink: 0 }}>%</span>
                </div>
                <p style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-xs)', color: 'var(--text-muted)' }}>
                  Refuse a run that would delete more than this share of the total library
                </p>
              </div>

              {/* Over-broad match % */}
              <div>
                <div style={{ marginBottom: 'var(--space-2)' }}>
                  <label
                    style={{
                      fontSize: 'var(--text-sm)',
                      fontWeight: 'var(--font-weight-medium)',
                      color: 'var(--text-secondary)',
                    }}
                  >
                    Over-broad match %
                  </label>
                </div>
                <div className="cap-field__row">
                  <label className="cap-field__enable">
                    <input
                      type="checkbox"
                      checked={form.overBroadEnabled}
                      onChange={e => setF('overBroadEnabled', e.target.checked)}
                      disabled={saving}
                    />
                    Enable
                  </label>
                  <div className="cap-field__input">
                    <Input
                      type="number"
                      value={form.overBroadPct}
                      onChange={e => setF('overBroadPct', e.target.value)}
                      min={0.1}
                      max={100}
                      step={0.1}
                      disabled={saving || !form.overBroadEnabled}
                      placeholder="30"
                    />
                  </div>
                  <span style={{ fontSize: 'var(--text-sm)', color: 'var(--text-muted)', flexShrink: 0 }}>%</span>
                </div>
                <p style={{ marginTop: 'var(--space-1)', fontSize: 'var(--text-xs)', color: 'var(--text-muted)' }}>
                  Warn before sweeping if a rule matches more than this share of the scanned library
                </p>
              </div>
            </div>
          </CardBody>
        </Card>

        {/* ── Dry-Run ── */}
        <Card className={form.globalDryRun ? 'settings-dryrun-card--active' : ''}>
          <CardBody>
            <p className="settings-section__label">Dry-Run Mode</p>
            <div className="settings-dryrun__body">
              <Toggle
                checked={form.globalDryRun}
                onChange={v => setF('globalDryRun', v)}
                label="Simulate only — nothing will be deleted"
                description="When on, all sweep runs are logged and reported but no files are removed and no Radarr/Sonarr changes are made."
                disabled={saving}
              />
              {form.globalDryRun && (
                <div className="settings-dryrun__banner">
                  <span className="settings-dryrun__banner-icon">
                    <Warning size={16} weight="fill" />
                  </span>
                  <span>
                    Dry-run is <strong>on</strong>. Sweeprr will analyse your library and build the
                    Sweep Queue, but will not delete anything or modify Radarr/Sonarr until you
                    turn this off.
                  </span>
                </div>
              )}
            </div>
          </CardBody>
        </Card>

        {/* ── Theme ── */}
        <Card>
          <CardBody>
            <p className="settings-section__label">Appearance</p>
            <div className="settings-theme__row">
              <span className="settings-theme__label">
                {theme === 'dark' ? 'Dark mode' : 'Light mode'}
              </span>
              <Button
                variant="secondary"
                size="sm"
                iconLeft={theme === 'dark' ? <Sun size={15} /> : <Moon size={15} />}
                onClick={toggleTheme}
              >
                Switch to {theme === 'dark' ? 'light' : 'dark'}
              </Button>
            </div>
          </CardBody>
        </Card>

        {/* ── About ── */}
        <Card>
          <CardBody>
            <p className="settings-section__label">About</p>
            <div className="settings-about__body">
              <div className="settings-about__row">
                <span className="settings-about__title">Sweeprr</span>
                {version && <span className="settings-about__version">v{version}</span>}
              </div>
              <p className="settings-about__description">
                Self-hosted media library management app. Integrates Jellyfin with Radarr/Sonarr to automatically sweep watched media.
              </p>
              <div className="settings-about__links">
                <a
                  href="https://github.com/lyfie-org/sweeprr"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="settings-about__link"
                >
                  GitHub
                </a>
                <span className="settings-about__divider">•</span>
                <a
                  href="https://github.com/lyfie-org/sweeprr/blob/main/LICENSE"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="settings-about__link"
                >
                  License
                </a>
                <span className="settings-about__divider">•</span>
                <a
                  href="/scalar/v1"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="settings-about__link"
                >
                  API Docs
                </a>
              </div>
            </div>
          </CardBody>
        </Card>

        {/* ── Footer ── */}
        <div className="settings-footer">
          <Button variant="primary" onClick={handleSave} loading={saving}>
            Save Changes
          </Button>
          {savedFlash && (
            <span className="settings-footer__saved">
              <CheckCircle size={16} weight="fill" />
              Saved
            </span>
          )}
        </div>

      </div>
    </div>
  )
}
