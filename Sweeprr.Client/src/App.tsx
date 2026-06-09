import { createBrowserRouter, RouterProvider, Navigate } from 'react-router-dom'
import { AppShell } from './components/layout/AppShell'
import { ProtectedRoute } from './components/router/ProtectedRoute'
import { LoginPage } from './pages/LoginPage'
import { SetupPage } from './pages/SetupPage'
import { DashboardPage } from './pages/DashboardPage'
import { SweepPage } from './pages/SweepPage'
import { RulesPage } from './pages/RulesPage'
import { ConnectionsPage } from './pages/ConnectionsPage'
import { ExclusionsPage } from './pages/ExclusionsPage'
import { SettingsPage } from './pages/SettingsPage'
import { LogsPage } from './pages/LogsPage'
import { KitchenSink } from './pages/KitchenSink'
import './styles/app.css'

const router = createBrowserRouter([
  // Dev kitchen sink (preserves existing ?ks shortcut via direct route)
  ...(import.meta.env.DEV ? [{ path: '/__kitchen-sink', element: <KitchenSink /> }] : []),

  { path: '/login', element: <LoginPage /> },
  { path: '/setup', element: <SetupPage /> },

  {
    element: <ProtectedRoute />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true,           element: <DashboardPage />   },
          { path: 'sweep',         element: <SweepPage />       },
          { path: 'rules',         element: <RulesPage />       },
          { path: 'connections',   element: <ConnectionsPage /> },
          { path: 'exclusions',    element: <ExclusionsPage />  },
          { path: 'settings',      element: <SettingsPage />    },
          { path: 'logs',          element: <LogsPage />        },
        ],
      },
    ],
  },

  // Fallback
  { path: '*', element: <Navigate to="/" replace /> },
])

export default function App() {
  return <RouterProvider router={router} />
}
