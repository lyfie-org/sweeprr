import { useEffect, useState } from 'react'
import { Modal, Button } from '../ui'
import type { RuleGroupResponse } from '../../api/rules'
import './ExcludeScopeModal.css'

interface ExcludeScopeModalProps {
  open: boolean
  onClose: () => void
  onConfirm: (ruleGroupId: number | null) => void
  ruleGroups: RuleGroupResponse[]
  count: number
  busy?: boolean
}

export function ExcludeScopeModal({
  open,
  onClose,
  onConfirm,
  ruleGroups,
  count,
  busy = false,
}: ExcludeScopeModalProps) {
  const [scope, setScope] = useState<'global' | 'group'>('global')
  const [groupId, setGroupId] = useState<number | ''>('')

  useEffect(() => {
    if (open) {
      setScope('global')
      setGroupId(ruleGroups[0]?.id ?? '')
    }
  }, [open, ruleGroups])

  const handleConfirm = () => {
    onConfirm(scope === 'global' ? null : (groupId === '' ? null : groupId))
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Add Exclusion"
      footer={
        <div style={{ display: 'flex', gap: 'var(--space-3)', justifyContent: 'flex-end', width: '100%' }}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button
            variant="primary"
            onClick={handleConfirm}
            loading={busy}
            disabled={scope === 'group' && groupId === ''}
          >
            Exclude {count} Item{count === 1 ? '' : 's'}
          </Button>
        </div>
      }
    >
      <div className="exclude-scope__body">
        <p className="exclude-scope__intro">
          Excluding {count} item{count === 1 ? '' : 's'} prevents the rule engine from flagging{' '}
          {count === 1 ? 'it' : 'them'} again, and removes any pending sweep queue entries.
        </p>

        <label className={`exclude-scope__option ${scope === 'global' ? 'exclude-scope__option--active' : ''}`}>
          <input
            type="radio"
            name="exclude-scope"
            checked={scope === 'global'}
            onChange={() => setScope('global')}
          />
          <div>
            <div className="exclude-scope__option-title">Global</div>
            <div className="exclude-scope__option-desc">Excluded from every rule group, permanently.</div>
          </div>
        </label>

        <label className={`exclude-scope__option ${scope === 'group' ? 'exclude-scope__option--active' : ''}`}>
          <input
            type="radio"
            name="exclude-scope"
            checked={scope === 'group'}
            onChange={() => setScope('group')}
          />
          <div className="exclude-scope__option-full">
            <div className="exclude-scope__option-title">Specific rule group</div>
            <div className="exclude-scope__option-desc">Only excluded from the selected rule group.</div>
            <select
              className="exclude-scope__select"
              value={groupId}
              disabled={scope !== 'group'}
              onChange={e => setGroupId(e.target.value === '' ? '' : Number(e.target.value))}
            >
              {ruleGroups.length === 0 && <option value="">No rule groups available</option>}
              {ruleGroups.map(g => (
                <option key={g.id} value={g.id}>{g.name}</option>
              ))}
            </select>
          </div>
        </label>
      </div>
    </Modal>
  )
}
