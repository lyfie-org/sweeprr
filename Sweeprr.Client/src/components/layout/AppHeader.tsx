import logoSrc from '../../assets/sweeprr_logo.png'
import './AppHeader.css'

interface AppHeaderProps {
  children?: React.ReactNode
}

export function AppHeader({ children }: AppHeaderProps) {
  return (
    <header className="app-header">
      <a className="app-header__logo" href="/" aria-label="Sweeprr home">
        <img
          src={logoSrc}
          alt="Sweeprr"
          className="app-header__logo-img"
        />
        <span className="app-header__wordmark">Sweeprr</span>
      </a>
      <div className="app-header__spacer" />
      {children}
    </header>
  )
}
