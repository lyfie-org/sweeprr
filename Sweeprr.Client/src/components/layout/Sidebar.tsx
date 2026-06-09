import { useState, useEffect } from 'react'
import { NavLink } from 'react-router-dom'
import {
  House, Broom, ListChecks, PlugsConnected, Funnel, Gear, Article, SignOut,
} from '@phosphor-icons/react'
import { useAuth } from '../../context/AuthContext'
import { RealtimePill } from './RealtimePill'
import { systemApi } from '../../api/system'
import './Sidebar.css'

interface NavItem {
  to: string
  icon: React.ReactNode
  label: string
}

const NAV_ITEMS: NavItem[] = [
  { to: '/',            icon: <House size={20} weight="duotone" />,          label: 'Dashboard'   },
  { to: '/sweep',       icon: <Broom size={20} weight="duotone" />,          label: 'Sweep Queue' },
  { to: '/rules',       icon: <ListChecks size={20} weight="duotone" />,     label: 'Rules'       },
  { to: '/connections', icon: <PlugsConnected size={20} weight="duotone" />, label: 'Connections' },
  { to: '/exclusions',  icon: <Funnel size={20} weight="duotone" />,         label: 'Exclusions'  },
  { to: '/settings',    icon: <Gear size={20} weight="duotone" />,           label: 'Settings'    },
  { to: '/logs',        icon: <Article size={20} weight="duotone" />,         label: 'Logs'        },
]

interface SidebarProps {
  collapsed: boolean
}

export function Sidebar({ collapsed }: SidebarProps) {
  const { user, logout } = useAuth()
  const [version, setVersion] = useState<string>('')

  useEffect(() => {
    systemApi.getInfo()
      .then(data => setVersion(data.version))
      .catch(() => {})
  }, [])

  return (
    <aside className={`sidebar${collapsed ? ' sidebar--collapsed' : ''}`}>
      <nav className="sidebar__nav" aria-label="Main navigation">
        {NAV_ITEMS.map(item => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/'}
            className={({ isActive }) =>
              `sidebar__link${isActive ? ' sidebar__link--active' : ''}`
            }
            title={collapsed ? item.label : undefined}
          >
            <span className="sidebar__link-icon" aria-hidden="true">{item.icon}</span>
            {!collapsed && <span className="sidebar__link-label">{item.label}</span>}
          </NavLink>
        ))}
      </nav>

      <div className="sidebar__footer">
        <RealtimePill collapsed={collapsed} />

        <button
          className="sidebar__logout"
          onClick={logout}
          title={collapsed ? `Sign out (${user?.username})` : undefined}
          aria-label="Sign out"
        >
          <SignOut size={18} weight="duotone" />
          {!collapsed && <span>{user?.username}</span>}
        </button>

        {!collapsed && version && (
          <div className="sidebar__version" data-testid="sidebar-version">
            v{version}
          </div>
        )}
      </div>
    </aside>
  )
}
