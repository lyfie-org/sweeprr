import { useCallback, useEffect, useState } from 'react'
import {
  Plus,
  ListChecks,
  PencilSimple,
  Trash,
  Play,
  FilmSlate,
  Television,
  VideoCamera,
  ClockClockwise,
} from '@phosphor-icons/react'
import {
  rulesApi,
  ACTION_LABELS,
  MEDIA_TYPE_LABELS,
  type MediaType,
  type RuleGroupResponse,
  type SweepAction,
} from '../api/rules'
import { ApiError } from '../api/client'
import {
  Badge,
  Button,
  Card,
  CardBody,
  CardFooter,
  Spinner,
  Toggle,
  useToast,
} from '../components/ui'
import { RuleGroupEditor } from '../components/rules'
import './RulesPage.css'

// ── Helpers ───────────────────────────────────────────────────────────────────

const MEDIA_ICONS: Record<MediaType, React.ReactNode> = {
  Movie:   <FilmSlate   size={15} weight="duotone" />,
  Series:  <Television  size={15} weight="duotone" />,
  Season:  <Television  size={15} weight="duotone" />,
  Episode: <VideoCamera size={15} weight="duotone" />,
}

const ACTION_VARIANTS: Record<SweepAction, 'danger' | 'warning' | 'info' | 'neutral'> = {
  DeleteAndUnmonitor:      'danger',
  DeleteOnly:              'danger',
  DeleteSeriesIfEmpty:     'warning',
  UnmonitorOnly:           'info',
  UnmonitorSeasonIfEmpty:  'info',
  ChangeQualityProfile:    'neutral',
}

function conditionSummary(count: number) {
  return count === 1 ? '1 condition' : `${count} conditions`
}

function relativeDate(iso: string) {
  const diff = Date.now() - new Date(iso).getTime()
  const d = Math.floor(diff / 86_400_000)
  if (d === 0) return 'Today'
  if (d === 1) return 'Yesterday'
  if (d < 30) return `${d}d ago`
  const m = Math.floor(d / 30)
  return m === 1 ? '1 month ago' : `${m} months ago`
}

// ── Rule Group Card ───────────────────────────────────────────────────────────

interface RuleGroupCardProps {
  group: RuleGroupResponse
  onEdit: () => void
  onRefresh: () => void
}

function RuleGroupCard({ group, onEdit, onRefresh }: RuleGroupCardProps) {
  const { toast } = useToast()
  const [scanning, setScanning] = useState(false)
  const [toggling, setToggling] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)

  async function handleScan() {
    setScanning(true)
    try {
      const res = await rulesApi.scan(group.id)
      toast({
        type: 'success',
        title: `Scan complete`,
        message: `${res.itemsFlagged} item(s) flagged in ${res.durationMs}ms`,
      })
      onRefresh()
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Scan failed'
      toast({ type: 'error', title: 'Scan failed', message: msg })
    } finally {
      setScanning(false)
    }
  }

  async function handleToggleEnabled() {
    setToggling(true)
    try {
      await rulesApi.update(group.id, {
        name: group.name,
        description: group.description,
        mediaType: group.mediaType,
        action: group.action,
        isEnabled: !group.isEnabled,
        cronOverride: group.cronOverride,
        conditions: group.conditions,
      })
      onRefresh()
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Update failed'
      toast({ type: 'error', title: 'Toggle failed', message: msg })
    } finally {
      setToggling(false)
    }
  }

  async function handleDelete() {
    if (!confirmDelete) {
      setConfirmDelete(true)
      setTimeout(() => setConfirmDelete(false), 3000)
      return
    }
    setDeleting(true)
    try {
      await rulesApi.delete(group.id)
      toast({ type: 'success', title: 'Rule group deleted' })
      onRefresh()
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Delete failed'
      toast({ type: 'error', title: 'Delete failed', message: msg })
      setDeleting(false)
      setConfirmDelete(false)
    }
  }

  return (
    <Card className={`rule-card${!group.isEnabled ? ' rule-card--disabled' : ''}`}>
      <CardBody>
        <div className="rule-card__header">
          <div className="rule-card__icon-col">
            <div className="rule-card__media-icon">
              {MEDIA_ICONS[group.mediaType]}
            </div>
          </div>

          <div className="rule-card__meta">
            <div className="rule-card__name-row">
              <h3 className="rule-card__name" title={group.name}>{group.name}</h3>
              {!group.isEnabled && (
                <Badge variant="neutral" size="sm">Disabled</Badge>
              )}
            </div>
            {group.description && (
              <p className="rule-card__description">{group.description}</p>
            )}
          </div>

          <div className="rule-card__toggle">
            <Toggle
              checked={group.isEnabled}
              onChange={handleToggleEnabled}
              label=""
              disabled={toggling || scanning || deleting}
            />
          </div>
        </div>

        <div className="rule-card__badges">
          <Badge variant="neutral" size="sm">
            {MEDIA_ICONS[group.mediaType]}
            {MEDIA_TYPE_LABELS[group.mediaType]}
          </Badge>
          <Badge variant={ACTION_VARIANTS[group.action]} size="sm">
            {ACTION_LABELS[group.action]}
          </Badge>
          <Badge variant="neutral" size="sm">
            <ListChecks size={12} weight="duotone" />
            {conditionSummary(group.conditions.length)}
          </Badge>
          {group.cronOverride && (
            <Badge variant="info" size="sm">
              <ClockClockwise size={12} weight="duotone" />
              {group.cronOverride}
            </Badge>
          )}
        </div>

        <div className="rule-card__footer-meta">
          Updated {relativeDate(group.updatedAt)}
        </div>
      </CardBody>

      <CardFooter>
        <div className="rule-card__actions">
          <Button
            variant="secondary"
            size="sm"
            iconLeft={<Play size={13} weight="fill" />}
            onClick={handleScan}
            loading={scanning}
            disabled={deleting || toggling || !group.isEnabled}
            title={!group.isEnabled ? 'Enable rule group to scan' : undefined}
          >
            Scan Now
          </Button>
          <Button
            variant="ghost"
            size="sm"
            iconLeft={<PencilSimple size={13} weight="duotone" />}
            onClick={onEdit}
            disabled={scanning || deleting}
          >
            Edit
          </Button>
          <Button
            variant={confirmDelete ? 'danger' : 'ghost'}
            size="sm"
            iconLeft={!confirmDelete ? <Trash size={13} weight="duotone" /> : undefined}
            onClick={handleDelete}
            loading={deleting}
            disabled={scanning || toggling}
          >
            {confirmDelete ? 'Confirm delete?' : 'Delete'}
          </Button>
        </div>
      </CardFooter>
    </Card>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function RulesPage() {
  const { toast } = useToast()
  const [groups, setGroups] = useState<RuleGroupResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [editorOpen, setEditorOpen] = useState(false)
  const [editingGroup, setEditingGroup] = useState<RuleGroupResponse | null>(null)

  const fetchGroups = useCallback(async () => {
    try {
      const data = await rulesApi.getAll()
      setGroups(data)
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Failed to load rule groups'
      toast({ type: 'error', title: 'Load failed', message: msg })
    } finally {
      setLoading(false)
    }
  }, [toast])

  useEffect(() => {
    fetchGroups()
  }, [fetchGroups])

  function openCreate() {
    setEditingGroup(null)
    setEditorOpen(true)
  }

  function openEdit(group: RuleGroupResponse) {
    setEditingGroup(group)
    setEditorOpen(true)
  }

  function handleSaved() {
    setEditorOpen(false)
    fetchGroups()
  }

  function handleClose() {
    setEditorOpen(false)
  }

  if (loading) {
    return (
      <div className="rules-page__loading">
        <Spinner size="lg" />
      </div>
    )
  }

  return (
    <div className="rules-page">
      <div className="rules-page__header">
        <div>
          <h1 className="rules-page__title">Rule Groups</h1>
          <p className="rules-page__subtitle">
            Define retention policies — items matching all conditions will be swept.
          </p>
        </div>
        <Button
          variant="primary"
          size="sm"
          iconLeft={<Plus size={16} weight="bold" />}
          onClick={openCreate}
        >
          New Rule Group
        </Button>
      </div>

      {groups.length === 0 ? (
        <Card>
          <CardBody>
            <div className="rules-empty">
              <div className="rules-empty__icon">
                <ListChecks size={52} weight="duotone" />
              </div>
              <h3 className="rules-empty__title">No rule groups yet</h3>
              <p className="rules-empty__body">
                Create a rule group to define your first automated cleanup policy. Sweeprr
                will scan your library against the rules on your chosen schedule.
              </p>
              <Button
                variant="primary"
                size="sm"
                iconLeft={<Plus size={15} weight="bold" />}
                onClick={openCreate}
              >
                Create First Rule Group
              </Button>
            </div>
          </CardBody>
        </Card>
      ) : (
        <div className="rules-grid">
          {groups.map(group => (
            <RuleGroupCard
              key={group.id}
              group={group}
              onEdit={() => openEdit(group)}
              onRefresh={fetchGroups}
            />
          ))}
        </div>
      )}

      {editorOpen && (
        <RuleGroupEditor
          key={editingGroup?.id ?? 'new'}
          editing={editingGroup}
          onClose={handleClose}
          onSaved={handleSaved}
        />
      )}
    </div>
  )
}
