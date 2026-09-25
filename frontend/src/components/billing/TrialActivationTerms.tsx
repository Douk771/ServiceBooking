import { Card } from '../ui/Card'
import { Button } from '../ui/Button'
import type { TrialStateDto } from '../../api/billing'

interface Props {
  activationTerms: NonNullable<TrialStateDto['activationTerms']>
  /** True when this is shown purely as "confirm what a SuperAdmin already granted you" (§363.1) —
   *  no activate button, just the acknowledgement action. */
  acknowledgementOnly?: boolean
  onActivate?: () => void
  onAcknowledge?: () => void
  activating?: boolean
  acknowledging?: boolean
}

/**
 * Full activation-terms text (Т1/Т5, API_CONTRACT_CYCLE18.md §362.1). The text is printed WHOLE and
 * VERBATIM — no truncation, no "read more" collapse that hides it before the button, no reformatting
 * — because it is what makes the "we only warn inside the cabinet, no email/SMS" scheme lawful
 * (`LEGAL_REVIEW_CYCLE18.md` §4, правило 1; §362.1 "правовая нагрузка"). This includes the honest
 * disclaimer that there is no other notification channel (Т5) — it must stay visible, not hidden.
 *
 * When `activationTerms.acknowledgementRequired` is true (SuperAdmin already granted the trial and
 * the owner hasn't confirmed reading the terms), this block is not closeable at all and offers only
 * the "Понятно" acknowledgement action — never a way to dismiss without confirming.
 */
export function TrialActivationTerms({
  activationTerms,
  acknowledgementOnly = false,
  onActivate,
  onAcknowledge,
  activating = false,
  acknowledging = false,
}: Props) {
  const mustAcknowledge = acknowledgementOnly || activationTerms.acknowledgementRequired

  return (
    <Card className="p-[26px] mb-6 border border-line">
      <h2 className="text-[15.5px] font-semibold text-ink mb-3">Условия пробного периода</h2>
      {/* Дословно, целиком — не сокращать и не собирать из кусочков (§362.1, §371 п.1). */}
      <p className="text-sm text-ink-soft leading-[1.6] whitespace-pre-line mb-5">{activationTerms.text}</p>

      {mustAcknowledge ? (
        <Button loading={acknowledging} onClick={onAcknowledge}>
          Понятно
        </Button>
      ) : (
        onActivate && (
          <Button loading={activating} onClick={onActivate}>
            Активировать пробный период
          </Button>
        )
      )}
    </Card>
  )
}
