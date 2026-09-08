import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Icon } from '../ui/Icon'
import type { ConsentStatus } from '../../types'

interface Props {
  status: ConsentStatus
}

const DISMISSED_KEY = 'legal-banner-dismissed-version'

/**
 * Non-blocking notice for an "Editorial" document change (US-37 п. 4). Unlike ConsentGate, there is
 * no server-side record of "banner seen" — dismissal is purely local (ARCHITECTURE.md §3, ключ по
 * версии in localStorage), keyed by the concatenation of the current document versions so a later
 * real change re-shows it even though the user already dismissed an earlier one.
 */
export function LegalUpdateBanner({ status }: Props) {
  const versionKey = status.documents.map((d) => `${d.type}:${d.version}`).sort().join('|')
  const [dismissed, setDismissed] = useState(() => localStorage.getItem(DISMISSED_KEY) === versionKey)

  if (!status.showBanner || dismissed) return null

  const handleDismiss = () => {
    localStorage.setItem(DISMISSED_KEY, versionKey)
    setDismissed(true)
  }

  return (
    <div className="bg-info-bg text-info px-4 py-2.5 text-sm flex items-center justify-center gap-3 flex-wrap">
      <span className="flex items-center gap-1.5">
        <Icon name="alert-circle" size={14} strokeWidth={1.8} />
        Документы обновлены —{' '}
        <Link to="/privacy" className="underline hover:no-underline">политика</Link>
        {' и '}
        <Link to="/terms" className="underline hover:no-underline">соглашение</Link>
      </span>
      <button onClick={handleDismiss} className="font-semibold hover:opacity-70 transition-opacity">
        Понятно
      </button>
    </div>
  )
}
