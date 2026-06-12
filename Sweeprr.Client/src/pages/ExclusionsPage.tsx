import { useCallback, useEffect, useState } from 'react'
import { Funnel, Plus, Trash, Tag, ProhibitInset } from '@phosphor-icons/react'
import {
  exclusionsApi,
  type ExclusionResponse,
  type TagExclusionResponse,
} from '../api/exclusions'
import { connectionsApi, type ConnectionResponse, type TagDto } from '../api/connections'
import { rulesApi, type RuleGroupResponse } from '../api/rules'
import {
  Button,
  Badge,
  Spinner,
  Modal,
  useToast,
} from '../components/ui'
import './ExclusionsPage.css'

// ── Tab type ──────────────────────────────────────────────────────────────────

type Tab = 'media' | 'tags'

// ── Media exclusions tab ──────────────────────────────────────────────────────

function MediaExclusionsTab() {
  const { toast } = useToast()
  const [exclusions, setExclusions] = useState<ExclusionResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [deletingId, setDeletingId] = useState<number | null>(null)

  const load = useCallback(async () => {
    try {
      const data = await exclusionsApi.getAll()
      setExclusions(data)
    } catch {
      toast({ variant: 'error', message: 'Failed to load exclusions.' })
    } finally {
      setLoading(false)
    }
  }, [toast])

  useEffect(() => { load() }, [load])

  const handleDelete = async (id: number) => {
    setDeletingId(id)
    try {
      await exclusionsApi.delete(id)
      setExclusions(prev => prev.filter(e => e.id !== id))
      toast({ variant: 'success', message: 'Exclusion removed.' })
    } catch {
      toast({ variant: 'error', message: 'Failed to remove exclusion.' })
    } finally {
      setDeletingId(null)
    }
  }

  if (loading) {
    return (
      <div className="excl-empty">
        <Spinner size={24} />
      </div>
    )
  }

  if (exclusions.length === 0) {
    return (
      <div className="excl-empty">
        <ProhibitInset size={40} weight="duotone" className="excl-empty__icon" />
        <p>No media exclusions. Items ignored from the Sweep Queue appear here.</p>
      </div>
    )
  }

  return (
    <div className="excl-table-wrap">
      <table className="excl-table">
        <thead>
          <tr>
            <th>Jellyfin ID</th>
            <th>Reason</th>
            <th>Scope</th>
            <th>Expires</th>
            <th>Created By</th>
            <th>Added</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {exclusions.map(e => (
            <tr key={e.id}>
              <td className="excl-table__mono">{e.mediaServerItemId}</td>
              <td>{e.reason ?? <span className="excl-muted">—</span>}</td>
              <td>
                {e.ruleGroupName
                  ? <Badge variant="info">{e.ruleGroupName}</Badge>
                  : <span className="excl-muted">Global</span>}
              </td>
              <td>
                {e.expiresAt
                  ? new Date(e.expiresAt).toLocaleDateString()
                  : <span className="excl-muted">Permanent</span>}
              </td>
              <td>
                {e.createdBy
                  ? <Badge variant="info">{e.createdBy}</Badge>
                  : <span className="excl-muted">Admin</span>}
              </td>
              <td className="excl-muted">{new Date(e.createdAt).toLocaleDateString()}</td>
              <td>
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label="Remove exclusion"
                  disabled={deletingId === e.id}
                  onClick={() => handleDelete(e.id)}
                >
                  <Trash size={16} weight="duotone" />
                </Button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ── Tag exclusions tab ────────────────────────────────────────────────────────

function TagExclusionsTab() {
  const { toast } = useToast()
  const [tagExclusions, setTagExclusions] = useState<TagExclusionResponse[]>([])
  const [connections, setConnections] = useState<ConnectionResponse[]>([])
  const [ruleGroups, setRuleGroups] = useState<RuleGroupResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [showAdd, setShowAdd] = useState(false)
  const [deletingId, setDeletingId] = useState<number | null>(null)

  const load = useCallback(async () => {
    try {
      const [tags, conns, groups] = await Promise.all([
        exclusionsApi.getTags(),
        connectionsApi.getAll(),
        rulesApi.getAll(),
      ])
      setTagExclusions(tags)
      // Only Radarr/Sonarr connections have tags
      setConnections(conns.filter(c => c.type !== 0))
      setRuleGroups(groups)
    } catch {
      toast({ variant: 'error', message: 'Failed to load tag exclusions.' })
    } finally {
      setLoading(false)
    }
  }, [toast])

  useEffect(() => { load() }, [load])

  const handleDelete = async (id: number) => {
    setDeletingId(id)
    try {
      await exclusionsApi.deleteTag(id)
      setTagExclusions(prev => prev.filter(t => t.id !== id))
      toast({ variant: 'success', message: 'Tag exclusion removed.' })
    } catch {
      toast({ variant: 'error', message: 'Failed to remove tag exclusion.' })
    } finally {
      setDeletingId(null)
    }
  }

  const handleAdded = (newExclusion: TagExclusionResponse) => {
    setTagExclusions(prev => [...prev, newExclusion])
    setShowAdd(false)
    toast({ variant: 'success', message: `Tag "${newExclusion.tagName}" added as exclusion.` })
  }

  if (loading) {
    return (
      <div className="excl-empty">
        <Spinner size={24} />
      </div>
    )
  }

  return (
    <>
      <div className="excl-toolbar">
        <p className="excl-toolbar__hint">
          Items carrying any of these *arr tags will be skipped during sweep queue reconciliation.
        </p>
        <Button variant="primary" size="sm" onClick={() => setShowAdd(true)}>
          <Plus size={16} weight="bold" />
          Add Tag Exclusion
        </Button>
      </div>

      {tagExclusions.length === 0 ? (
        <div className="excl-empty">
          <Tag size={40} weight="duotone" className="excl-empty__icon" />
          <p>No tag exclusions configured.</p>
        </div>
      ) : (
        <div className="excl-table-wrap">
          <table className="excl-table">
            <thead>
              <tr>
                <th>Tag</th>
                <th>Connection</th>
                <th>Scope</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {tagExclusions.map(t => (
                <tr key={t.id}>
                  <td>
                    <span className="excl-tag-chip">
                      <Tag size={13} weight="fill" />
                      {t.tagName}
                    </span>
                  </td>
                  <td>{t.connectionName}</td>
                  <td>
                    {t.ruleGroupName
                      ? <Badge variant="info">{t.ruleGroupName}</Badge>
                      : <span className="excl-muted">Global</span>}
                  </td>
                  <td>
                    <Button
                      variant="ghost"
                      size="sm"
                      aria-label="Remove tag exclusion"
                      disabled={deletingId === t.id}
                      onClick={() => handleDelete(t.id)}
                    >
                      <Trash size={16} weight="duotone" />
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {showAdd && (
        <AddTagExclusionModal
          connections={connections}
          ruleGroups={ruleGroups}
          onClose={() => setShowAdd(false)}
          onAdded={handleAdded}
        />
      )}
    </>
  )
}

// ── Add tag exclusion modal ───────────────────────────────────────────────────

interface AddTagExclusionModalProps {
  connections: ConnectionResponse[]
  ruleGroups: RuleGroupResponse[]
  onClose: () => void
  onAdded: (exclusion: TagExclusionResponse) => void
}

function AddTagExclusionModal({
  connections,
  ruleGroups,
  onClose,
  onAdded,
}: AddTagExclusionModalProps) {
  const { toast } = useToast()
  const [selectedConnId, setSelectedConnId] = useState<number | ''>('')
  const [tags, setTags] = useState<TagDto[]>([])
  const [tagsLoading, setTagsLoading] = useState(false)
  const [selectedTagId, setSelectedTagId] = useState<number | ''>('')
  const [scopeGroupId, setScopeGroupId] = useState<number | ''>('')
  const [saving, setSaving] = useState(false)

  const loadTags = useCallback(async (connId: number) => {
    setTagsLoading(true)
    setTags([])
    setSelectedTagId('')
    try {
      const data = await connectionsApi.getTags(connId)
      setTags(data)
    } catch {
      toast({ variant: 'error', message: 'Failed to fetch tags from connection.' })
    } finally {
      setTagsLoading(false)
    }
  }, [toast])

  const handleConnChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const val = e.target.value
    if (val === '') {
      setSelectedConnId('')
      setTags([])
    } else {
      const id = parseInt(val, 10)
      setSelectedConnId(id)
      loadTags(id)
    }
  }

  const handleSave = async () => {
    if (selectedConnId === '' || selectedTagId === '') return
    const tag = tags.find(t => t.id === selectedTagId)
    if (!tag) return

    setSaving(true)
    try {
      const result = await exclusionsApi.createTag({
        tagName: tag.label,
        tagId: tag.id,
        serverConnectionId: selectedConnId as number,
        ruleGroupId: scopeGroupId === '' ? null : (scopeGroupId as number),
      })
      onAdded(result)
    } catch {
      toast({ variant: 'error', message: 'Failed to create tag exclusion.' })
    } finally {
      setSaving(false)
    }
  }

  const canSave = selectedConnId !== '' && selectedTagId !== '' && !saving

  const footer = (
    <>
      <Button variant="ghost" onClick={onClose}>Cancel</Button>
      <Button variant="primary" disabled={!canSave} onClick={handleSave}>
        {saving ? <Spinner size={14} /> : <Plus size={14} weight="bold" />}
        Add Exclusion
      </Button>
    </>
  )

  return (
    <Modal open title="Add Tag Exclusion" onClose={onClose} footer={footer}>
      <div className="excl-modal-body">
        <div className="excl-field">
          <label className="excl-label">Connection</label>
          <select
            className="excl-select"
            value={selectedConnId}
            onChange={handleConnChange}
          >
            <option value="">Select a Radarr or Sonarr connection…</option>
            {connections.map(c => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </select>
        </div>

        <div className="excl-field">
          <label className="excl-label">Tag</label>
          {tagsLoading ? (
            <div className="excl-select-loading"><Spinner size={16} /> Loading tags…</div>
          ) : (
            <select
              className="excl-select"
              value={selectedTagId}
              disabled={tags.length === 0}
              onChange={e => setSelectedTagId(e.target.value === '' ? '' : parseInt(e.target.value, 10))}
            >
              <option value="">
                {tags.length === 0
                  ? (selectedConnId === '' ? 'Select a connection first' : 'No tags found')
                  : 'Select a tag…'}
              </option>
              {tags.map(t => (
                <option key={t.id} value={t.id}>{t.label}</option>
              ))}
            </select>
          )}
        </div>

        <div className="excl-field">
          <label className="excl-label">Scope</label>
          <select
            className="excl-select"
            value={scopeGroupId}
            onChange={e => setScopeGroupId(e.target.value === '' ? '' : parseInt(e.target.value, 10))}
          >
            <option value="">Global (applies to all rule groups)</option>
            {ruleGroups.map(rg => (
              <option key={rg.id} value={rg.id}>{rg.name}</option>
            ))}
          </select>
          <p className="excl-hint">
            Global exclusions block the item across all rule groups. Scoped exclusions only
            apply when that specific rule group runs.
          </p>
        </div>
      </div>
    </Modal>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function ExclusionsPage() {
  const [activeTab, setActiveTab] = useState<Tab>('media')

  return (
    <div className="excl-page">
      <header className="excl-header">
        <div className="excl-header__title">
          <Funnel size={24} weight="duotone" />
          <h1>Exclusions</h1>
        </div>
        <p className="excl-header__subtitle">
          Excluded items are never added to the sweep queue.
        </p>
      </header>

      <div className="excl-tabs">
        <button
          className={`excl-tab${activeTab === 'media' ? ' excl-tab--active' : ''}`}
          onClick={() => setActiveTab('media')}
        >
          <ProhibitInset size={16} weight="duotone" />
          Media Exclusions
        </button>
        <button
          className={`excl-tab${activeTab === 'tags' ? ' excl-tab--active' : ''}`}
          onClick={() => setActiveTab('tags')}
        >
          <Tag size={16} weight="duotone" />
          Tag Exclusions
        </button>
      </div>

      <div className="excl-content">
        {activeTab === 'media' ? <MediaExclusionsTab /> : <TagExclusionsTab />}
      </div>
    </div>
  )
}
