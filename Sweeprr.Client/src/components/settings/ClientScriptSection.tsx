import { useState } from 'react'
import { Copy, Check } from '@phosphor-icons/react'
import { Badge, Card, CardBody } from '../ui'
import './ClientScriptSection.css'

interface ClientScriptSectionProps {
  publicBaseUrl: string | null
}

export function ClientScriptSection({ publicBaseUrl }: ClientScriptSectionProps) {
  const [copiedUrl, setCopiedUrl] = useState(false)
  const [copiedTag, setCopiedTag] = useState(false)

  const baseUrl = (publicBaseUrl?.trim() || window.location.origin).replace(/\/+$/, '')
  const scriptUrl = `${baseUrl}/api/integrations/jellyfin/client-script.js`
  const scriptTag = `<script src="${scriptUrl}"></script>`

  const copy = async (text: string, setCopied: (v: boolean) => void) => {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      setCopied(false)
    }
  }

  return (
    <Card>
      <CardBody>
        <p className="settings-section__label">Client Script Injection</p>
        <p className="client-script__hint">
          Show a "Leaving Soon" banner directly inside Jellyfin's web UI for items pending removal,
          with a link to request an extension.
        </p>

        <div className="client-script__step">
          <p className="client-script__step-title">1. Copy the script URL</p>
          <div className="client-script__box">
            <code>{scriptUrl}</code>
            <button
              type="button"
              className="client-script__copy-btn"
              onClick={() => copy(scriptUrl, setCopiedUrl)}
              aria-label="Copy script URL"
            >
              {copiedUrl ? <Check size={16} weight="bold" /> : <Copy size={16} />}
            </button>
          </div>
        </div>

        <div className="client-script__step">
          <p className="client-script__step-title">
            2. In Jellyfin, go to Dashboard → General → Custom JavaScript and paste this tag
          </p>
          <div className="client-script__box">
            <code>{scriptTag}</code>
            <button
              type="button"
              className="client-script__copy-btn"
              onClick={() => copy(scriptTag, setCopiedTag)}
              aria-label="Copy script tag"
            >
              {copiedTag ? <Check size={16} weight="bold" /> : <Copy size={16} />}
            </button>
          </div>
        </div>

        <Badge variant="success">
          Once active, Sweeprr will display expiry notices directly inside Jellyfin.
        </Badge>
      </CardBody>
    </Card>
  )
}
