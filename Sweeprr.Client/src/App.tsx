import { Broom } from '@phosphor-icons/react'
import './styles/app.css'

export default function App() {
  return (
    <div className="boot-screen">
      <div className="boot-card">
        <div className="boot-logo">
          <Broom size={48} weight="duotone" color="var(--accent)" />
        </div>
        <h1 className="boot-title">Sweeprr</h1>
        <p className="boot-subtitle">Media cleanup for self-hosters.</p>
        <div className="boot-pulse" aria-label="Loading" />
      </div>
    </div>
  )
}
