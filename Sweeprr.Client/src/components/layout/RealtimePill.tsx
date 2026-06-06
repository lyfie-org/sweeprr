import { useEffect, useState } from 'react'
import { Lightning } from '@phosphor-icons/react'
import { api } from '../../api/client'
import './RealtimePill.css'

type WsState = 'Connected' | 'Connecting' | 'Reconnecting' | 'Disconnected'

interface JellyfinStatusResponse {
  state: WsState
  lastConnectedAt: string | null
}

interface RealtimePillProps {
  collapsed?: boolean
}

export function RealtimePill({ collapsed = false }: RealtimePillProps) {
  const [state, setState] = useState<WsState>('Disconnected')

  useEffect(() => {
    let cancelled = false

    const poll = async () => {
      try {
        const res = await api.get<JellyfinStatusResponse>('/api/jellyfin/status')
        if (!cancelled) setState(res.state)
      } catch {
        if (!cancelled) setState('Disconnected')
      }
    }

    poll()
    const id = setInterval(poll, 30_000)
    return () => { cancelled = true; clearInterval(id) }
  }, [])

  const label =
    state === 'Connected'    ? 'Realtime'     :
    state === 'Reconnecting' ? 'Reconnecting' :
    state === 'Connecting'   ? 'Connecting'   : 'Offline'

  const mod =
    state === 'Connected'    ? 'connected'    :
    state === 'Reconnecting' ? 'reconnecting' :
    state === 'Connecting'   ? 'reconnecting' : 'offline'

  return (
    <div className={`realtime-pill realtime-pill--${mod}${collapsed ? ' realtime-pill--collapsed' : ''}`} title={label}>
      <Lightning size={14} weight="fill" className="realtime-pill__icon" />
      {!collapsed && (
        <>
          <span className="realtime-pill__label">{label}</span>
          <span className="realtime-pill__dot" aria-hidden="true" />
        </>
      )}
    </div>
  )
}
