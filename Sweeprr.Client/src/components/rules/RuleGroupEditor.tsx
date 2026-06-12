import './RuleGroupEditor.css'
import { useCallback, useEffect, useRef, useState } from 'react'
import {
  X,
  Plus,
  ArrowsDownUp,
  FloppyDisk,
  Eye,
} from '@phosphor-icons/react'
import {
  rulesApi,
  inferValueType,
  type FieldDescriptor,
  type MediaType,
  type RuleComparator,
  type RuleConditionDto,
  type RuleGroupRequest,
  type RuleGroupResponse,
  type SweepAction,
  ACTION_LABELS,
  MEDIA_TYPE_LABELS,
} from '../../api/rules'
import { connectionsApi, type ConnectionResponse, type QualityProfileDto } from '../../api/connections'
import { ApiError } from '../../api/client'
import { Button, Input, Spinner, Toggle, useToast } from '../ui'
import { ConditionRow } from './ConditionRow'

// ── Types ─────────────────────────────────────────────────────────────────────

interface Section {
  /** A unique key for React rendering */
  key: string
  conditions: ConditionDraft[]
}

interface ConditionDraft extends RuleConditionDto {
  /** Unique key for React rendering */
  draftKey: string
}

interface Props {
  /** null = create mode; non-null = edit mode */
  editing: RuleGroupResponse | null
  onClose: () => void
  onSaved: () => void
}

// ── Helpers ───────────────────────────────────────────────────────────────────

let _uid = 0
function uid() { return `k${++_uid}` }

const ALL_MEDIA_TYPES: MediaType[] = ['Movie', 'Series', 'Season', 'Episode']
const ALL_ACTIONS: SweepAction[] = [
  'DeleteAndUnmonitor',
  'UnmonitorOnly',
  'DeleteOnly',
  'DeleteSeriesIfEmpty',
  'UnmonitorSeasonIfEmpty',
  'ChangeQualityProfile',
]

function buildSections(conditions: RuleConditionDto[]): Section[] {
  if (conditions.length === 0) return []
  const bySection = new Map<number, RuleConditionDto[]>()
  for (const c of conditions) {
    const arr = bySection.get(c.section) ?? []
    arr.push(c)
    bySection.set(c.section, arr)
  }
  return Array.from(bySection.entries())
    .sort(([a], [b]) => a - b)
    .map(([, conds]) => ({
      key: uid(),
      conditions: conds.map(c => ({ ...c, draftKey: uid() })),
    }))
}

function buildDefaultCondition(fields: FieldDescriptor[]): ConditionDraft {
  const f = fields[0]
  const comp = f.allowedComparators[0] as RuleComparator
  return {
    draftKey: uid(),
    section: 0,
    logicalOperator: null,
    field: f.field,
    comparator: comp,
    value: '',
    valueType: inferValueType(comp, f.primaryValueType),
  }
}

function flattenSections(sections: Section[]): RuleConditionDto[] {
  return sections.flatMap((sec, sIdx) =>
    sec.conditions.map((c, cIdx) => ({
      section: sIdx,
      logicalOperator: cIdx === 0 ? null : c.logicalOperator,
      field: c.field,
      comparator: c.comparator,
      value: c.value,
      valueType: c.valueType,
    })),
  )
}

// ── Preview chip ──────────────────────────────────────────────────────────────

interface PreviewState {
  loading: boolean
  count: number | null
  titles: string[]
  note: string | null
}

// ── Editor ────────────────────────────────────────────────────────────────────

export function RuleGroupEditor({ editing, onClose, onSaved }: Props) {
  const { toast } = useToast()
  const isEditing = editing !== null

  // ── Form state ────────────────────────────────────────────────────────────
  const [name, setName] = useState(editing?.name ?? '')
  const [description, setDescription] = useState(editing?.description ?? '')
  const [mediaType, setMediaType] = useState<MediaType>(editing?.mediaType ?? 'Movie')
  const [action, setAction] = useState<SweepAction>(editing?.action ?? 'DeleteAndUnmonitor')
  const [targetProfileId, setTargetProfileId] = useState<number | null>(editing?.targetQualityProfileId ?? null)
  const [targetProfileName, setTargetProfileName] = useState<string | null>(editing?.targetQualityProfileName ?? null)
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfileDto[]>([])
  const [profilesLoading, setProfilesLoading] = useState(false)
  const [isEnabled, setIsEnabled] = useState(editing?.isEnabled ?? true)
  const [cronOverride, setCronOverride] = useState(editing?.cronOverride ?? '')
  const [cronError, setCronError] = useState<string | null>(null)
  const [nameError, setNameError] = useState<string | null>(null)

  // ── Field metadata ────────────────────────────────────────────────────────
  const [fields, setFields] = useState<FieldDescriptor[]>([])
  const [fieldsLoading, setFieldsLoading] = useState(true)

  // ── Connections (for Tags field) ──────────────────────────────────────────
  const [connections, setConnections] = useState<ConnectionResponse[]>([])

  // ── Sections + conditions ─────────────────────────────────────────────────
  const [sections, setSections] = useState<Section[]>([])

  // ── Preview chip ──────────────────────────────────────────────────────────
  const [preview, setPreview] = useState<PreviewState>({
    loading: false,
    count: null,
    titles: [],
    note: null,
  })
  const previewTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  // ── Saving state ──────────────────────────────────────────────────────────
  const [saving, setSaving] = useState(false)

  // ── MediaType change guard ────────────────────────────────────────────────
  const [pendingMediaType, setPendingMediaType] = useState<MediaType | null>(null)

  // ── Load field metadata + connections on mount ────────────────────────────
  useEffect(() => {
    Promise.all([
      rulesApi.getFieldsMeta(),
      connectionsApi.getAll(),
    ]).then(([meta, conns]) => {
      setFields(meta.fields)
      setConnections(conns)

      // Build initial sections (after we have field metadata)
      if (editing && editing.conditions.length > 0) {
        setSections(buildSections(editing.conditions))
      } else {
        // New: start with one condition
        setSections([{
          key: uid(),
          conditions: [buildDefaultCondition(meta.fields)],
        }])
      }
    }).catch(() => {
      toast({ type: 'error', title: 'Failed to load rule metadata' })
    }).finally(() => setFieldsLoading(false))
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  // ── Focus trap (Esc to close) ─────────────────────────────────────────────
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  // ── Prevent body scroll while panel is open ───────────────────────────────
  useEffect(() => {
    document.body.style.overflow = 'hidden'
    return () => { document.body.style.overflow = '' }
  }, [])

  // ── Preview — debounced after any condition change ────────────────────────
  const schedulePreview = useCallback(() => {
    if (previewTimerRef.current) clearTimeout(previewTimerRef.current)
    previewTimerRef.current = setTimeout(async () => {
      const flat = flattenSections(sections)
      if (flat.length === 0) {
        setPreview({ loading: false, count: null, titles: [], note: null })
        return
      }
      setPreview(p => ({ ...p, loading: true }))
      try {
        const res = await rulesApi.preview({ mediaType, conditions: flat })
        setPreview({ loading: false, count: res.matchCount, titles: res.sampleTitles, note: res.note })
      } catch {
        setPreview({ loading: false, count: null, titles: [], note: 'Preview unavailable' })
      }
    }, 800)
  }, [sections, mediaType])

  useEffect(() => {
    schedulePreview()
    return () => { if (previewTimerRef.current) clearTimeout(previewTimerRef.current) }
  }, [schedulePreview])

  // ── Fetch quality profiles when action = ChangeQualityProfile ────────────
  useEffect(() => {
    if (action !== 'ChangeQualityProfile') return
    const arrConn = connections.find(c => c.type === 1 || c.type === 2)
    if (!arrConn) return
    setProfilesLoading(true)
    connectionsApi.getQualityProfiles(arrConn.id)
      .then(profiles => setQualityProfiles(profiles))
      .catch(() => setQualityProfiles([]))
      .finally(() => setProfilesLoading(false))
  }, [action, connections])

  // ── Arr connection for Tags field ─────────────────────────────────────────
  const arrConnection = connections.find(c => c.type === 1 || c.type === 2) ?? null

  // ── Validation ────────────────────────────────────────────────────────────
  function validate(): boolean {
    let ok = true
    if (!name.trim()) { setNameError('Name is required'); ok = false }
    else setNameError(null)

    if (cronOverride.trim()) {
      // Basic 5-field cron check — server validates authoritatively
      const parts = cronOverride.trim().split(/\s+/)
      if (parts.length !== 5) { setCronError('Invalid cron expression (need 5 fields)'); ok = false }
      else setCronError(null)
    } else setCronError(null)

    return ok
  }

  // ── Save ──────────────────────────────────────────────────────────────────
  async function handleSave() {
    if (!validate()) return
    const flat = flattenSections(sections)
    if (flat.length === 0) {
      toast({ type: 'warning', title: 'Add at least one condition before saving.' })
      return
    }
    setSaving(true)
    const req: RuleGroupRequest = {
      name: name.trim(),
      description: description.trim() || null,
      mediaType,
      action,
      targetQualityProfileId: action === 'ChangeQualityProfile' ? targetProfileId : null,
      targetQualityProfileName: action === 'ChangeQualityProfile' ? targetProfileName : null,
      isEnabled,
      cronOverride: cronOverride.trim() || null,
      conditions: flat,
    }
    try {
      if (isEditing) {
        await rulesApi.update(editing.id, req)
        toast({ type: 'success', title: 'Rule group updated' })
      } else {
        await rulesApi.create(req)
        toast({ type: 'success', title: 'Rule group created' })
      }
      onSaved()
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Save failed'
      toast({ type: 'error', title: 'Save failed', message: msg })
    } finally {
      setSaving(false)
    }
  }

  // ── MediaType change with guard ────────────────────────────────────────────
  function handleMediaTypeChange(mt: MediaType) {
    if (sections.some(s => s.conditions.length > 0)) {
      setPendingMediaType(mt)
    } else {
      setMediaType(mt)
    }
  }

  function confirmMediaTypeChange() {
    if (!pendingMediaType || fields.length === 0) return
    setMediaType(pendingMediaType)
    // Reset conditions — old ones may be incompatible
    setSections([{ key: uid(), conditions: [buildDefaultCondition(fields)] }])
    setPendingMediaType(null)
  }

  // ── Section / condition mutations ─────────────────────────────────────────
  function addCondition(sectionIdx: number) {
    if (fields.length === 0) return
    const def = buildDefaultCondition(fields)
    // Second+ condition in a section always defaults to And
    def.logicalOperator = 'And'
    setSections(prev => prev.map((sec, i) =>
      i === sectionIdx ? { ...sec, conditions: [...sec.conditions, def] } : sec,
    ))
  }

  function addSection() {
    if (fields.length === 0) return
    const def = buildDefaultCondition(fields)
    // New section — its first condition's logicalOperator is null (section-level Or handled visually)
    setSections(prev => [...prev, { key: uid(), conditions: [def] }])
  }

  function updateCondition(sectionIdx: number, condIdx: number, updated: RuleConditionDto) {
    setSections(prev => prev.map((sec, si) =>
      si !== sectionIdx ? sec : {
        ...sec,
        conditions: sec.conditions.map((c, ci) =>
          ci !== condIdx ? c : { ...c, ...updated },
        ),
      },
    ))
  }

  function removeCondition(sectionIdx: number, condIdx: number) {
    setSections(prev => {
      const next = prev.map((sec, si) => {
        if (si !== sectionIdx) return sec
        const newConds = sec.conditions.filter((_, ci) => ci !== condIdx)
        return { ...sec, conditions: newConds }
      }).filter(sec => sec.conditions.length > 0) // remove empty sections
      // Ensure at least one section remains
      if (next.length === 0 && fields.length > 0) {
        return [{ key: uid(), conditions: [buildDefaultCondition(fields)] }]
      }
      return next
    })
  }

  // ── Render ────────────────────────────────────────────────────────────────
  const totalConditions = sections.reduce((n, s) => n + s.conditions.length, 0)
  const canSave = !saving && name.trim().length > 0 && totalConditions > 0

  return (
    <>
      {/* Backdrop */}
      <div className="rule-editor-backdrop" onClick={onClose} aria-hidden="true" />

      {/* Slide-in panel */}
      <div
        className="rule-editor-panel"
        role="dialog"
        aria-modal="true"
        aria-label={isEditing ? `Edit rule group: ${editing.name}` : 'New rule group'}
      >
        {/* Panel header */}
        <div className="rule-editor-panel__header">
          <div className="rule-editor-panel__title">
            <h2>{isEditing ? 'Edit Rule Group' : 'New Rule Group'}</h2>
            {isEditing && (
              <span className="rule-editor-panel__subtitle">{editing.name}</span>
            )}
          </div>
          <button
            type="button"
            className="rule-editor-panel__close"
            onClick={onClose}
            aria-label="Close editor"
          >
            <X size={18} />
          </button>
        </div>

        {/* Scrollable body */}
        <div className="rule-editor-panel__body">
          {fieldsLoading ? (
            <div className="rule-editor-panel__loading">
              <Spinner size="lg" />
            </div>
          ) : (
            <>
              {/* ── Meta section ─────────────────────────────────────────── */}
              <section className="rule-editor-section">
                <h3 className="rule-editor-section__title">Details</h3>

                <Input
                  id="re-name"
                  label="Name"
                  value={name}
                  onChange={e => { setName(e.target.value); setNameError(null) }}
                  placeholder="e.g. Old watched movies"
                  error={nameError ?? undefined}
                  required
                  disabled={saving}
                />

                <Input
                  id="re-desc"
                  label="Description"
                  value={description}
                  onChange={e => setDescription(e.target.value)}
                  placeholder="Optional description"
                  disabled={saving}
                />

                <div className="rule-editor-row">
                  <div className="rule-editor-field">
                    <label className="rule-editor-label">Media Type</label>
                    <div className="rule-editor-select-wrap">
                      <select
                        id="re-mediatype"
                        className="rule-editor-select"
                        value={mediaType}
                        onChange={e => handleMediaTypeChange(e.target.value as MediaType)}
                        disabled={saving}
                      >
                        {ALL_MEDIA_TYPES.map(mt => (
                          <option key={mt} value={mt}>{MEDIA_TYPE_LABELS[mt]}</option>
                        ))}
                      </select>
                    </div>
                  </div>

                  <div className="rule-editor-field">
                    <label className="rule-editor-label">Action</label>
                    <div className="rule-editor-select-wrap">
                      <select
                        id="re-action"
                        className="rule-editor-select"
                        value={action}
                        onChange={e => setAction(e.target.value as SweepAction)}
                        disabled={saving}
                      >
                        {ALL_ACTIONS.map(a => (
                          <option key={a} value={a}>{ACTION_LABELS[a]}</option>
                        ))}
                      </select>
                    </div>
                  </div>
                </div>

                {action === 'ChangeQualityProfile' && (
                  <div className="rule-editor-row">
                    <div className="rule-editor-field">
                      <label className="rule-editor-label">Target Quality Profile</label>
                      <div className="rule-editor-select-wrap">
                        <select
                          id="re-quality-profile"
                          className="rule-editor-select"
                          value={targetProfileId ?? ''}
                          onChange={e => {
                            const id = e.target.value ? Number(e.target.value) : null
                            const name = qualityProfiles.find(p => p.id === id)?.name ?? null
                            setTargetProfileId(id)
                            setTargetProfileName(name)
                          }}
                          disabled={saving || profilesLoading}
                        >
                          <option value="">
                            {profilesLoading ? 'Loading profiles…' : '— Select a profile —'}
                          </option>
                          {qualityProfiles.map(p => (
                            <option key={p.id} value={p.id}>{p.name}</option>
                          ))}
                        </select>
                      </div>
                      {!profilesLoading && qualityProfiles.length === 0 && (
                        <p className="rule-editor-section__hint" style={{ marginTop: 4 }}>
                          No profiles found — ensure a Radarr or Sonarr connection is configured.
                        </p>
                      )}
                    </div>
                  </div>
                )}

                <div className="rule-editor-row">
                  <div className="rule-editor-field">
                    <Input
                      id="re-cron"
                      label="Cron override (optional)"
                      value={cronOverride}
                      onChange={e => { setCronOverride(e.target.value); setCronError(null) }}
                      placeholder="0 3 * * 0  (default schedule if blank)"
                      error={cronError ?? undefined}
                      helper="5-field cron. Blank = use global schedule."
                      disabled={saving}
                    />
                  </div>
                </div>

                <Toggle
                  checked={isEnabled}
                  onChange={setIsEnabled}
                  label="Enable this rule group"
                  description="Disabled groups are skipped during scheduled scans"
                  disabled={saving}
                />
              </section>

              {/* ── Conditions section ────────────────────────────────────── */}
              <section className="rule-editor-section">
                <h3 className="rule-editor-section__title">Conditions</h3>
                <p className="rule-editor-section__hint">
                  Conditions within a section combine with <strong>AND/OR</strong>.
                  Multiple sections are joined with <strong>OR</strong>.
                </p>

                {sections.map((sec, sIdx) => (
                  <div key={sec.key} className="rule-editor-section-band">
                    {/* Section-level OR separator (except first) */}
                    {sIdx > 0 && (
                      <div className="section-separator">
                        <span className="section-separator__line" />
                        <span className="section-separator__label">OR</span>
                        <span className="section-separator__line" />
                      </div>
                    )}

                    <div className="condition-list">
                      {sec.conditions.map((cond, cIdx) => (
                        <ConditionRow
                          key={cond.draftKey}
                          condition={cond}
                          isFirst={cIdx === 0}
                          fields={fields}
                          arrConnectionId={arrConnection?.id ?? null}
                          onChange={updated => updateCondition(sIdx, cIdx, updated)}
                          onRemove={() => removeCondition(sIdx, cIdx)}
                          disabled={saving}
                        />
                      ))}
                    </div>

                    <Button
                      variant="ghost"
                      size="sm"
                      iconLeft={<Plus size={13} weight="bold" />}
                      onClick={() => addCondition(sIdx)}
                      disabled={saving}
                    >
                      Add condition
                    </Button>
                  </div>
                ))}

                <Button
                  variant="ghost"
                  size="sm"
                  iconLeft={<ArrowsDownUp size={13} weight="duotone" />}
                  onClick={addSection}
                  disabled={saving}
                  className="rule-editor-add-section"
                >
                  Add OR section
                </Button>
              </section>
            </>
          )}
        </div>

        {/* Sticky footer with preview chip + save */}
        <div className="rule-editor-panel__footer">
          <div className="rule-editor-preview">
            {preview.loading ? (
              <span className="rule-editor-preview__chip rule-editor-preview__chip--loading">
                <Spinner size="sm" />
                Calculating…
              </span>
            ) : preview.count !== null ? (
              <span
                className={`rule-editor-preview__chip${preview.count > 0 ? ' rule-editor-preview__chip--match' : ''}`}
                title={preview.titles.length > 0 ? `Sample: ${preview.titles.join(', ')}` : undefined}
              >
                <Eye size={13} weight="duotone" />
                Would match: <strong>{preview.count}</strong> item{preview.count !== 1 ? 's' : ''}
                {preview.note && <span className="rule-editor-preview__note"> · {preview.note}</span>}
              </span>
            ) : (
              <span className="rule-editor-preview__chip rule-editor-preview__chip--empty">
                <Eye size={13} weight="duotone" />
                Preview will appear here
              </span>
            )}
          </div>

          <div className="rule-editor-panel__footer-actions">
            <Button variant="ghost" onClick={onClose} disabled={saving}>
              Cancel
            </Button>
            <Button
              variant="primary"
              onClick={handleSave}
              loading={saving}
              disabled={!canSave}
              iconLeft={<FloppyDisk size={15} weight="duotone" />}
            >
              {isEditing ? 'Save Changes' : 'Create Rule Group'}
            </Button>
          </div>
        </div>
      </div>

      {/* MediaType change confirmation */}
      {pendingMediaType !== null && (
        <div className="rule-editor-confirm-backdrop">
          <div className="rule-editor-confirm">
            <h4>Change media type?</h4>
            <p>
              Switching to <strong>{MEDIA_TYPE_LABELS[pendingMediaType]}</strong> will reset all
              existing conditions, as some fields may not be compatible.
            </p>
            <div className="rule-editor-confirm__actions">
              <Button variant="ghost" size="sm" onClick={() => setPendingMediaType(null)}>
                Cancel
              </Button>
              <Button variant="danger" size="sm" onClick={confirmMediaTypeChange}>
                Reset & Switch
              </Button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
