import { Card } from '../ui/Card'
import type { TrialWarningDto } from '../../api/billing'

interface Props {
  warning: TrialWarningDto
}

/**
 * The `TrialExpired` warning, rendered WITHOUT a close button (Т3, API_CONTRACT_CYCLE18.md §362.2,
 * §371 п.10) — this is the only message that reaches an owner who never opened the cabinet during
 * the trial, and the server guarantees `dismissible: false` / `visibleUntilUtc` ≥30 days for it.
 * No "seen" flag is written anywhere (not localStorage, not session state): the component simply has
 * no dismiss affordance, on every render, for as long as the server keeps sending this warning.
 *
 * Deliberately a separate component from `TrialBanner` rather than a variant of it — mixing the two
 * risks a future edit accidentally adding a close button to this one.
 */
export function TrialExpiredNotice({ warning }: Props) {
  return (
    <Card className="p-5 mb-6 border border-danger bg-danger-bg">
      <p className="text-sm font-semibold text-ink mb-1">{warning.text}</p>
      {warning.affected.length > 0 && (
        <ul className="text-xs text-ink-soft list-disc list-inside">
          {warning.affected.map((a) => (
            <li key={a}>{a}</li>
          ))}
        </ul>
      )}
    </Card>
  )
}
