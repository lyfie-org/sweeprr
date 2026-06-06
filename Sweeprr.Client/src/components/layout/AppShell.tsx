import { useState } from 'react'
import { Outlet } from 'react-router-dom'
import { List } from '@phosphor-icons/react'
import { Sidebar } from './Sidebar'
import { AppHeader } from './AppHeader'
import './AppShell.css'

export function AppShell() {
  const [collapsed, setCollapsed] = useState(false)

  return (
    <div className="app-shell">
      <Sidebar collapsed={collapsed} />
      <div className="app-shell__main">
        <AppHeader>
          <button
            className="app-shell__toggle"
            onClick={() => setCollapsed(v => !v)}
            aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          >
            <List size={20} />
          </button>
        </AppHeader>
        <main className="app-shell__content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
