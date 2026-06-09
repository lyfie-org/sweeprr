import { useEffect, useState } from 'react'
import { Trash } from '@phosphor-icons/react'
import {
  type FieldDescriptor,
  type RuleComparator,
  type RuleConditionDto,
  type RuleValueType,
  COMPARATOR_LABELS,
  VALUELESS_COMPARATORS,
  inferValueType,
} from '../../api/rules'
import { connectionsApi, type DiskSpaceDto } from '../../api/connections'
import { TagMultiselect } from './TagMultiselect'
import { GenreMultiselect } from './GenreMultiselect'
import { Toggle } from '../ui';

const DISK_FIELDS = new Set(['DiskFreeSpacePercent', 'DiskFreeSpaceGb'])

interface ConditionRowProps {
  condition: RuleConditionDto
  isFirst: boolean          // first in its section — no LogicalOperator selector
  fields: FieldDescriptor[]
  /** ID of the arr connection to fetch Tags from (null if not configured) */
  arrConnectionId: number | null
  onChange: (updated: RuleConditionDto) => void
  onRemove: () => void
  disabled?: boolean
}

export function ConditionRow({
  condition,
  isFirst,
  fields,
  arrConnectionId,
  onChange,
  onRemove,
  disabled,
}: ConditionRowProps) {
  const currentField = fields.find(f => f.field === condition.field)

  // Disk space helper text — fetch once per connection when a disk field is selected
  const [diskStats, setDiskStats] = useState<DiskSpaceDto | null>(null)
  useEffect(() => {
    if (!DISK_FIELDS.has(condition.field) || arrConnectionId === null) {
      setDiskStats(null)
      return
    }
    let cancelled = false
    connectionsApi.getDiskSpace(arrConnectionId)
      .then(d => { if (!cancelled) setDiskStats(d) })
      .catch(() => { if (!cancelled) setDiskStats(null) })
    return () => { cancelled = true }
  }, [condition.field, arrConnectionId])

  function handleFieldChange(newField: string) {
    const fieldMeta = fields.find(f => f.field === newField)
    if (!fieldMeta) return
    // Reset comparator to first valid one for the new field
    const firstComp = fieldMeta.allowedComparators[0] as RuleComparator
    const newValueType = inferValueType(firstComp, fieldMeta.primaryValueType)
    onChange({
      ...condition,
      field: newField,
      comparator: firstComp,
      value: '',
      valueType: newValueType,
    })
  }

  function handleComparatorChange(comp: RuleComparator) {
    const vt: RuleValueType = currentField
      ? inferValueType(comp, currentField.primaryValueType)
      : condition.valueType
    onChange({
      ...condition,
      comparator: comp,
      value: VALUELESS_COMPARATORS.has(comp) ? '' : condition.value,
      valueType: vt,
    })
  }

  function handleValueChange(raw: string) {
    onChange({ ...condition, value: raw })
  }

  function handleTagsChange(labels: string[]) {
    onChange({ ...condition, value: labels.join(',') })
  }

  const isValueless = VALUELESS_COMPARATORS.has(condition.comparator)
  const allowedComps = currentField?.allowedComparators ?? []

  // Derive value control type from valueType
  const tagValues = condition.value
    ? condition.value.split(',').map(s => s.trim()).filter(Boolean)
    : []

  return (
    <div className="condition-row">
      {/* Logical operator chip (hidden for first row in section) */}
      <div className="condition-row__op">
        {!isFirst && (
          <button
            type="button"
            className={`condition-op-chip condition-op-chip--${condition.logicalOperator?.toLowerCase() ?? 'and'}`}
            onClick={() =>
              onChange({
                ...condition,
                logicalOperator: condition.logicalOperator === 'And' ? 'Or' : 'And',
              })
            }
            disabled={disabled}
            title="Click to toggle And/Or"
          >
            {condition.logicalOperator ?? 'And'}
          </button>
        )}
      </div>

      {/* Field selector */}
      <div className="condition-row__field">
        <div className="condition-row__select-wrap">
          <select
            className="condition-row__select"
            value={condition.field}
            onChange={e => handleFieldChange(e.target.value)}
            disabled={disabled}
          >
            {fields.map(f => (
              <option key={f.field} value={f.field}>
                {f.label}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Comparator selector */}
      <div className="condition-row__comparator">
        <div className="condition-row__select-wrap">
          <select
            className="condition-row__select"
            value={condition.comparator}
            onChange={e => handleComparatorChange(e.target.value as RuleComparator)}
            disabled={disabled}
          >
            {allowedComps.map(c => (
              <option key={c} value={c}>
                {COMPARATOR_LABELS[c]}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Value control — dynamic by valueType */}
      <div className="condition-row__value">
        {!isValueless && renderValueControl()}
        {DISK_FIELDS.has(condition.field) && diskStats !== null && (
          <span className="condition-row__disk-hint">
            Current: {diskStats.freePercent}% free
            ({diskStats.freeSpaceGb} GB of {diskStats.totalSpaceGb} GB)
          </span>
        )}
      </div>

      {/* Remove button */}
      <button
        type="button"
        className="condition-row__remove"
        onClick={onRemove}
        disabled={disabled}
        aria-label="Remove condition"
        title="Remove condition"
      >
        <Trash size={14} weight="duotone" />
      </button>
    </div>
  )

  function renderValueControl() {
    switch (condition.valueType) {
      case 'Bool':
        return (
          <Toggle
            checked={condition.value === 'true'}
            onChange={v => handleValueChange(v ? 'true' : 'false')}
            label=""
            disabled={disabled}
          />
        )
      case 'Date':
        return (
          <input
            type="date"
            className="condition-row__input"
            value={condition.value}
            onChange={e => handleValueChange(e.target.value)}
            disabled={disabled}
          />
        )
      case 'RelativeDays':
        return (
          <div className="condition-row__days-wrap">
            <input
              type="number"
              className="condition-row__input condition-row__input--days"
              min={1}
              value={condition.value}
              onChange={e => handleValueChange(e.target.value)}
              placeholder="30"
              disabled={disabled}
            />
            <span className="condition-row__days-label">days</span>
          </div>
        )
      case 'Number':
        if (condition.field === 'ResolutionHeight') {
          return (
            <div className="condition-row__select-wrap">
              <select
                className="condition-row__select"
                value={condition.value}
                onChange={e => handleValueChange(e.target.value)}
                disabled={disabled}
              >
                <option value="">Select resolution...</option>
                <option value="2160">4K (2160p)</option>
                <option value="1080">1080p</option>
                <option value="720">720p</option>
                <option value="480">480p</option>
              </select>
            </div>
          )
        }
        return (
          <input
            type="number"
            className="condition-row__input"
            value={condition.value}
            onChange={e => handleValueChange(e.target.value)}
            placeholder="0"
            disabled={disabled}
          />
        )
      case 'TextList':
        if (condition.field === 'Genre') {
          return (
            <GenreMultiselect
              value={tagValues}
              onChange={handleTagsChange}
              disabled={disabled}
            />
          )
        }
        return (
          <TagMultiselect
            connectionId={arrConnectionId}
            value={tagValues}
            onChange={handleTagsChange}
            disabled={disabled}
          />
        )
      default:
        // Text
        return (
          <input
            type="text"
            className="condition-row__input"
            value={condition.value}
            onChange={e => handleValueChange(e.target.value)}
            placeholder="Value…"
            disabled={disabled}
          />
        )
    }
  }
}
