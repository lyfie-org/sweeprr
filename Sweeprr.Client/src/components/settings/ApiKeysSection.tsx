import { useCallback, useEffect, useState } from 'react'
import { Plus, Trash, Copy, Check, Warning } from '@phosphor-icons/react'
import {
  apiKeysApi,
  API_KEY_SCOPES,
  type ApiKeyResponse,
  type ApiKeyScope,
  type GenerateApiKeyResponse,
} from '../../api/apiKeys'
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
  useToast,
} from '../ui'
import './ApiKeysSection.css'

const SCOPE_LABELS: Record<ApiKeyScope, string> = {
  'read:sweep': 'Read Sweep',
  'write:sweep': 'Write Sweep',
  'execute:sweep': 'Execute Sweep',
  'admin': 'Admin',
}

function formatDate(iso: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString()
}

export function ApiKeysSection() {
  const { toast } = useToast()
  const [keys, setKeys] = useState<ApiKeyResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [showGenerate, setShowGenerate] = useState(false)
  const [revealKey, setRevealKey] = useState<GenerateApiKeyResponse | null>(null)

  const fetchKeys = useCallback(async () => {
    try {
      const data = await apiKeysApi.getAll()
      setKeys(data)
    } catch {
      toast({ type: 'error', title: 'Failed to load API keys' })
    } finally {
      setLoading(false)
    }
  }, [toast])

  useEffect(() => {
    fetchKeys()
  }, [fetchKeys])

  const handleRevoke = async (id: number) => {
    try {
      await apiKeysApi.revoke(id)
      setKeys(prev => prev.map(k => (k.id === id ? { ...k, isActive: false } : k)))
      toast({ type: 'success', title: 'API key revoked' })
    } catch {
      toast({ type: 'error', title: 'Failed to revoke API key' })
    }
  }

  const handleGenerated = (response: GenerateApiKeyResponse) => {
    setShowGenerate(false)
    setRevealKey(response)
    fetchKeys()
  }

  return (
    <Card>
      <CardBody>
        <div className="api-keys__header">
          <p className="settings-section__label" style={{ margin: 0 }}>API Keys</p>
          <Button
            variant="secondary"
            size="sm"
            iconLeft={<Plus size={14} weight="bold" />}
            onClick={() => setShowGenerate(true)}
          >
            Generate Key
          </Button>
        </div>
        <p className="api-keys__hint">
          API keys authenticate external scripts and integrations against the Sweeprr API.
          Each key carries one or more scopes that limit what it can do.
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
                <TableHeaderCell>Scopes</TableHeaderCell>
                <TableHeaderCell>Created</TableHeaderCell>
                <TableHeaderCell>Expires</TableHeaderCell>
                <TableHeaderCell>Last Used</TableHeaderCell>
                <TableHeaderCell>{null}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {keys.length === 0 ? (
                <TableEmpty>No API keys yet.</TableEmpty>
              ) : (
                keys.map(k => (
                  <TableRow key={k.id}>
                    <TableCell>
                      <div className="api-keys__name">
                        {k.name}
                        {!k.isActive && <Badge variant="danger" size="sm">Revoked</Badge>}
                      </div>
                      <div className="api-keys__masked">{k.maskedKey}</div>
                    </TableCell>
                    <TableCell>
                      <div className="api-keys__scopes">
                        {k.scopes.map(s => (
                          <Badge key={s} variant="info" size="sm">{SCOPE_LABELS[s] ?? s}</Badge>
                        ))}
                      </div>
                    </TableCell>
                    <TableCell>{formatDate(k.createdAt)}</TableCell>
                    <TableCell>{formatDate(k.expiresAt)}</TableCell>
                    <TableCell>{formatDate(k.lastUsedAt)}</TableCell>
                    <TableCell>
                      {k.isActive && (
                        <Button
                          variant="ghost"
                          size="sm"
                          aria-label={`Revoke ${k.name}`}
                          onClick={() => handleRevoke(k.id)}
                        >
                          <Trash size={16} weight="duotone" />
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        )}
      </CardBody>

      {showGenerate && (
        <GenerateApiKeyModal
          onClose={() => setShowGenerate(false)}
          onGenerated={handleGenerated}
        />
      )}

      {revealKey && (
        <ApiKeyRevealModal
          response={revealKey}
          onClose={() => setRevealKey(null)}
        />
      )}
    </Card>
  )
}

// ── Generate modal ──────────────────────────────────────────────────────────

interface GenerateApiKeyModalProps {
  onClose: () => void
  onGenerated: (response: GenerateApiKeyResponse) => void
}

function GenerateApiKeyModal({ onClose, onGenerated }: GenerateApiKeyModalProps) {
  const { toast } = useToast()
  const [name, setName] = useState('')
  const [scopes, setScopes] = useState<ApiKeyScope[]>([])
  const [expiresAt, setExpiresAt] = useState('')
  const [saving, setSaving] = useState(false)

  const toggleScope = (scope: ApiKeyScope) => {
    setScopes(prev =>
      prev.includes(scope) ? prev.filter(s => s !== scope) : [...prev, scope]
    )
  }

  const handleSave = async () => {
    if (!name.trim() || scopes.length === 0) return
    setSaving(true)
    try {
      const response = await apiKeysApi.generate({
        name: name.trim(),
        scopes,
        expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
      })
      onGenerated(response)
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Failed to generate API key'
      toast({ type: 'error', title: 'Generate failed', message: msg })
    } finally {
      setSaving(false)
    }
  }

  const canSave = name.trim().length > 0 && scopes.length > 0 && !saving

  const footer = (
    <>
      <Button variant="ghost" onClick={onClose} disabled={saving}>Cancel</Button>
      <Button variant="primary" disabled={!canSave} onClick={handleSave}>
        {saving ? <Spinner size="sm" /> : <Plus size={14} weight="bold" />}
        Generate Key
      </Button>
    </>
  )

  return (
    <Modal open title="Generate API Key" onClose={onClose} footer={footer}>
      <div className="api-keys__form">
        <Input
          label="Name"
          value={name}
          onChange={e => setName(e.target.value)}
          placeholder="e.g. Home Assistant integration"
          disabled={saving}
        />

        <div>
          <label className="api-keys__scope-label">Scopes</label>
          <div className="api-keys__scope-list">
            {API_KEY_SCOPES.map(scope => (
              <label key={scope} className="api-keys__scope-option">
                <input
                  type="checkbox"
                  checked={scopes.includes(scope)}
                  onChange={() => toggleScope(scope)}
                  disabled={saving}
                />
                {SCOPE_LABELS[scope]}
              </label>
            ))}
          </div>
        </div>

        <Input
          label="Expiration (optional)"
          type="date"
          value={expiresAt}
          onChange={e => setExpiresAt(e.target.value)}
          disabled={saving}
          helper="Leave blank for a key that never expires"
        />
      </div>
    </Modal>
  )
}

// ── Reveal modal ─────────────────────────────────────────────────────────────

interface ApiKeyRevealModalProps {
  response: GenerateApiKeyResponse
  onClose: () => void
}

function ApiKeyRevealModal({ response, onClose }: ApiKeyRevealModalProps) {
  const [copied, setCopied] = useState(false)

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(response.rawKey)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }

  const footer = (
    <Button variant="primary" disabled={!copied} onClick={onClose}>
      {copied ? 'Done' : 'Copy the key to continue'}
    </Button>
  )

  return (
    <Modal
      open
      title="API Key Generated"
      onClose={() => { if (copied) onClose() }}
      footer={footer}
    >
      <div className="api-keys__reveal">
        <div className="api-keys__reveal-warning">
          <Warning size={16} weight="fill" />
          <span>{response.warning} Copy this key now — it will not be shown again.</span>
        </div>

        <div className="api-keys__reveal-box">
          <code>{response.rawKey}</code>
          <button
            type="button"
            className="api-keys__copy-btn"
            onClick={handleCopy}
            aria-label="Copy API key"
          >
            {copied ? <Check size={16} weight="bold" /> : <Copy size={16} />}
          </button>
        </div>

        <div className="api-keys__scopes">
          {response.scopes.map(s => (
            <Badge key={s} variant="info" size="sm">{SCOPE_LABELS[s] ?? s}</Badge>
          ))}
        </div>
      </div>
    </Modal>
  )
}
