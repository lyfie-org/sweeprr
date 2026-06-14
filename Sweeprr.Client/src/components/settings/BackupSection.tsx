import { useCallback, useEffect, useState } from 'react'
import { CloudArrowUp, Database } from '@phosphor-icons/react'
import {
  backupApi,
  BACKUP_DESTINATION_TYPES,
  type BackupDestinationType,
  type BackupHistoryEntry,
  type BackupSettingResponse,
  type UpdateBackupSettingRequest,
} from '../../api/backup'
import { ApiError } from '../../api/client'
import { describeCron } from '../../pages/SettingsPage'
import {
  Button,
  Card,
  CardBody,
  Input,
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
import './BackupSection.css'

const DESTINATION_LABELS: Record<BackupDestinationType, string> = {
  Local: 'Local',
  S3: 'S3 / MinIO',
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const units = ['KB', 'MB', 'GB']
  let value = bytes / 1024
  let i = 0
  while (value >= 1024 && i < units.length - 1) {
    value /= 1024
    i++
  }
  return `${value.toFixed(1)} ${units[i]}`
}

export function BackupSection() {
  const { toast } = useToast()
  const [settings, setSettings] = useState<BackupSettingResponse | null>(null)
  const [history, setHistory] = useState<BackupHistoryEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [backingUp, setBackingUp] = useState(false)

  const [isEnabled, setIsEnabled] = useState(false)
  const [destinationType, setDestinationType] = useState<BackupDestinationType>('Local')
  const [localPath, setLocalPath] = useState('')
  const [s3Endpoint, setS3Endpoint] = useState('')
  const [s3Region, setS3Region] = useState('')
  const [s3Bucket, setS3Bucket] = useState('')
  const [s3AccessKey, setS3AccessKey] = useState('')
  const [s3SecretKey, setS3SecretKey] = useState('')
  const [retentionCount, setRetentionCount] = useState(5)
  const [scheduleCron, setScheduleCron] = useState('0 3 * * 0')

  const fetchAll = useCallback(async () => {
    try {
      const [settingsData, historyData] = await Promise.all([
        backupApi.get(),
        backupApi.getHistory(),
      ])
      setSettings(settingsData)
      setHistory(historyData)
      setIsEnabled(settingsData.isEnabled)
      setDestinationType(settingsData.destinationType)
      setLocalPath(settingsData.localPath ?? '')
      setS3Endpoint(settingsData.s3Endpoint ?? '')
      setS3Region(settingsData.s3Region ?? '')
      setS3Bucket(settingsData.s3Bucket ?? '')
      setS3AccessKey(settingsData.s3AccessKey ?? '')
      setRetentionCount(settingsData.retentionCount)
      setScheduleCron(settingsData.scheduleCron)
    } catch {
      toast({ type: 'error', title: 'Failed to load backup settings' })
    } finally {
      setLoading(false)
    }
  }, [toast])

  useEffect(() => {
    fetchAll()
  }, [fetchAll])

  const handleSave = async () => {
    setSaving(true)
    try {
      const req: UpdateBackupSettingRequest = {
        isEnabled,
        destinationType,
        localPath: localPath.trim(),
        s3Endpoint: s3Endpoint.trim(),
        s3Region: s3Region.trim(),
        s3Bucket: s3Bucket.trim(),
        s3AccessKey: s3AccessKey.trim(),
        retentionCount,
        scheduleCron: scheduleCron.trim(),
      }
      if (s3SecretKey.trim()) req.s3SecretKey = s3SecretKey.trim()

      const updated = await backupApi.update(req)
      setSettings(updated)
      setS3SecretKey('')
      toast({ type: 'success', title: 'Backup settings saved' })
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Failed to save backup settings'
      toast({ type: 'error', title: 'Save failed', message: msg })
    } finally {
      setSaving(false)
    }
  }

  const handleBackupNow = async () => {
    setBackingUp(true)
    try {
      const result = await backupApi.trigger()
      if (result.success) {
        toast({
          type: 'success',
          title: 'Backup completed',
          message: `${result.filename} (${result.sizeKb?.toLocaleString() ?? '?'} KB)`,
        })
        setHistory(await backupApi.getHistory())
      } else {
        toast({ type: 'error', title: 'Backup failed', message: result.error ?? undefined })
      }
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Backup failed'
      toast({ type: 'error', title: 'Backup failed', message: msg })
    } finally {
      setBackingUp(false)
    }
  }

  const cronPreview = describeCron(scheduleCron)

  if (loading) {
    return (
      <Card>
        <CardBody>
          <div style={{ display: 'flex', justifyContent: 'center', padding: 'var(--space-6)' }}>
            <Spinner size="md" />
          </div>
        </CardBody>
      </Card>
    )
  }

  return (
    <Card>
      <CardBody>
        <div className="backup__header">
          <p className="settings-section__label" style={{ margin: 0 }}>Backup &amp; Restore</p>
          <Button
            variant="secondary"
            size="sm"
            iconLeft={<CloudArrowUp size={14} weight="bold" />}
            loading={backingUp}
            onClick={handleBackupNow}
            disabled={backingUp}
          >
            Back Up Now
          </Button>
        </div>
        <p className="backup__hint">
          Snapshot the Sweeprr database and encryption keys to a local directory or an
          S3/MinIO-compatible bucket, on a schedule or on demand.
        </p>

        <div className="settings-section__fields">
          <Toggle
            checked={isEnabled}
            onChange={setIsEnabled}
            label="Scheduled backups"
            description="Automatically run a backup on the schedule below."
            disabled={saving}
          />

          <div>
            <label className="backup__field-label">Destination</label>
            <div className="backup__radio-group">
              {BACKUP_DESTINATION_TYPES.map(opt => (
                <button
                  key={opt}
                  type="button"
                  className={destinationType === opt ? 'active' : ''}
                  onClick={() => setDestinationType(opt)}
                  disabled={saving}
                >
                  {DESTINATION_LABELS[opt]}
                </button>
              ))}
            </div>
          </div>

          {destinationType === 'Local' ? (
            <Input
              label="Local path"
              value={localPath}
              onChange={e => setLocalPath(e.target.value)}
              placeholder="/config/backups"
              disabled={saving}
              style={{ fontFamily: 'var(--font-mono)' }}
            />
          ) : (
            <>
              <Input
                label="S3 endpoint"
                value={s3Endpoint}
                onChange={e => setS3Endpoint(e.target.value)}
                placeholder="http://minio:9000 (leave blank for AWS S3)"
                disabled={saving}
                style={{ fontFamily: 'var(--font-mono)' }}
              />
              <Input
                label="Region"
                value={s3Region}
                onChange={e => setS3Region(e.target.value)}
                placeholder="us-east-1"
                disabled={saving}
              />
              <Input
                label="Bucket"
                value={s3Bucket}
                onChange={e => setS3Bucket(e.target.value)}
                placeholder="sweeprr-backups"
                disabled={saving}
              />
              <Input
                label="Access key"
                value={s3AccessKey}
                onChange={e => setS3AccessKey(e.target.value)}
                disabled={saving}
                style={{ fontFamily: 'var(--font-mono)' }}
              />
              <Input
                label="Secret key"
                type="password"
                value={s3SecretKey}
                onChange={e => setS3SecretKey(e.target.value)}
                placeholder={settings?.maskedS3SecretKey ? `Current: ${settings.maskedS3SecretKey}` : 'Enter secret key'}
                helper={settings?.maskedS3SecretKey ? 'Leave blank to keep the existing secret key' : undefined}
                disabled={saving}
              />
            </>
          )}

          <Input
            label="Retention count"
            type="number"
            min={1}
            max={20}
            value={retentionCount}
            onChange={e => setRetentionCount(Math.min(20, Math.max(1, Number(e.target.value) || 1)))}
            disabled={saving}
            helper="Number of most-recent backups to keep; older backups are deleted automatically."
          />

          <Input
            label="Schedule (cron)"
            value={scheduleCron}
            onChange={e => setScheduleCron(e.target.value)}
            placeholder="0 3 * * 0"
            disabled={saving}
            style={{ fontFamily: 'var(--font-mono)' }}
            helper={cronPreview ? cronPreview.label : 'Invalid cron expression'}
          />

          {isEnabled && settings?.nextScheduledRun && (
            <p className="backup__next-run">
              Next scheduled run: {new Date(settings.nextScheduledRun).toLocaleString()}
            </p>
          )}

          <div className="backup__save-row">
            <Button variant="primary" loading={saving} onClick={handleSave} disabled={saving}>
              Save Backup Settings
            </Button>
          </div>
        </div>

        <div className="backup__history">
          <p className="backup__history-label">Backup History</p>
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>Filename</TableHeaderCell>
                <TableHeaderCell>Size</TableHeaderCell>
                <TableHeaderCell>Created</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {history.length === 0 ? (
                <TableEmpty>No backups yet.</TableEmpty>
              ) : (
                history.map(h => (
                  <TableRow key={h.filename}>
                    <TableCell>
                      <div className="backup__filename">
                        <Database size={14} weight="duotone" />
                        {h.filename}
                      </div>
                    </TableCell>
                    <TableCell>{formatBytes(h.sizeBytes)}</TableCell>
                    <TableCell>{new Date(h.createdAt).toLocaleString()}</TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </div>
      </CardBody>
    </Card>
  )
}
