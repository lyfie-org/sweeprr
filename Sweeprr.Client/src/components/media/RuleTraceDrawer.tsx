import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { X, CheckCircle, XCircle, MinusCircle } from '@phosphor-icons/react'
import { mediaApi, type RuleTraceResponse } from '../../api/media'
import { COMPARATOR_LABELS } from '../../api/rules'
import { Badge, Spinner } from '../ui'
import './RuleTraceDrawer.css'

interface RuleTraceDrawerProps {
  itemId: string | null
  onClose: () => void
}

function humanizeField(field: string): string {
  return field.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
}

export function RuleTraceDrawer({ itemId, onClose }: RuleTraceDrawerProps) {
  const [trace, setTrace] = useState<RuleTraceResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!itemId) {
      setTrace(null)
      setError(null)
      return
    }
    setLoading(true)
    setError(null)
    mediaApi.getRuleTrace(itemId)
      .then(setTrace)
      .catch((err: any) => setError(err.message || 'Failed to load rule trace.'))
      .finally(() => setLoading(false))
  }, [itemId])

  useEffect(() => {
    if (!itemId) return
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKey)
    return () => document.removeEventListener('keydown', handleKey)
  }, [itemId, onClose])

  if (!itemId) return null

  return createPortal(
    <div
      className="trace-drawer-backdrop"
      onClick={e => { if (e.target === e.currentTarget) onClose() }}
    >
      <div className="trace-drawer" role="dialog" aria-modal="true" aria-label="Rule trace">
        <div className="trace-drawer__header">
          <div>
            <h2 className="trace-drawer__title">{trace?.title ?? 'Rule Trace'}</h2>
            <p className="trace-drawer__subtitle">How this item evaluates against every rule group of its type</p>
          </div>
          <button className="trace-drawer__close" onClick={onClose} aria-label="Close">
            <X size={18} weight="bold" />
          </button>
        </div>

        <div className="trace-drawer__body">
          {loading ? (
            <div className="trace-drawer__loading"><Spinner size="lg" /></div>
          ) : error ? (
            <div className="trace-drawer__error">{error}</div>
          ) : trace && trace.evaluations.length > 0 ? (
            trace.evaluations.map(evaluation => (
              <div key={evaluation.ruleGroupId} className="trace-group">
                <div className="trace-group__header">
                  <span className="trace-group__name">{evaluation.ruleGroupName}</span>
                  <Badge variant={evaluation.matched ? 'danger' : 'neutral'}>
                    {evaluation.matched ? 'Matches' : 'No Match'}
                  </Badge>
                </div>
                <div className="trace-group__clauses">
                  {evaluation.clauseResults.map((clause, idx) => (
                    <div key={idx} className="trace-clause">
                      {idx > 0 && clause.logicalOperator && (
                        <span className="trace-clause__op">{clause.logicalOperator.toUpperCase()}</span>
                      )}
                      <span className="trace-clause__field">{humanizeField(clause.field)}</span>
                      <span className="trace-clause__comparator">{COMPARATOR_LABELS[clause.comparator]}</span>
                      {clause.value && <span className="trace-clause__value">{clause.value}</span>}
                      <span className="trace-clause__result">
                        {clause.result === true && (
                          <CheckCircle size={16} weight="fill" className="trace-clause__icon trace-clause__icon--true" />
                        )}
                        {clause.result === false && (
                          <XCircle size={16} weight="fill" className="trace-clause__icon trace-clause__icon--false" />
                        )}
                        {clause.result === null && (
                          <MinusCircle size={16} weight="fill" className="trace-clause__icon trace-clause__icon--null" />
                        )}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            ))
          ) : (
            <div className="trace-drawer__empty">No rule groups apply to this media type.</div>
          )}
        </div>
      </div>
    </div>,
    document.body,
  )
}
