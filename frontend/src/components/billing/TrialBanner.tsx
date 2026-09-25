import { useState } from 'react'
import { Card } from '../ui/Card'
import type { TrialWarningDto } from '../../api/billing'

interface Props {
  warning: TrialWarningDto
}

/**
 * Closeable reminder banner for `trial.warning` (API_CONTRACT_CYCLE18.md §362.2). This is the ONLY
 * closeable surface for trial warnings — `dismissible` is decided by the server, never by the
 * client, and when it's `false` (always the case for `TrialExpired`, Т3) this component renders no
 * close button at all and does not persist any "seen" flag anywhere (not localStorage, not session
 * state) — `TrialExpiredNotice` covers that state separately and unconditionally.
 *
 * `affected` renders immediately below the text (§362.2): legal copy refers to it as "перечисленные
 * ниже", so moving the list elsewhere would make that reference false.
 */
export function TrialBanner({ warning }: Props) {
  const [dismissed, setDismissed] = useState(false)
  if (dismissed) return null

  const isDanger = warning.code === 'TrialExpired' || warning.code === 'TrialOverLimit'

  return (
    <Card className={`p-5 mb-6 border ${isDanger ? 'border-danger bg-danger-bg' : 'border-warning bg-[#FBF3E3]'}`}>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-ink mb-1">{warning.text}</p>
          {warning.affected.length > 0 && (
            <ul className="text-xs text-ink-soft list-disc list-inside">
              {warning.affected.map((a) => (
                <li key={a}>{a}</li>
              ))}
            </ul>
          )}
        </div>
        {warning.dismissible && (
          <button
            type="button"
            aria-label="Закрыть"
            onClick={() => setDismissed(true)}
            className="shrink-0 text-ink-soft hover:text-ink text-lg leading-none min-h-[44px] min-w-[44px] flex items-center justify-center"
          >
            ×
          </button>
        )}
      </div>
    </Card>
  )
}
