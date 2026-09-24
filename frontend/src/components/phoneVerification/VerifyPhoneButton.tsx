import { useEffect, useState } from 'react'
import { Button } from '../ui/Button'
import { VerifyPhoneDialog } from './VerifyPhoneDialog'
import { PhoneVerifiedBadge } from './PhoneVerifiedBadge'
import { usePhoneVerification, usePhoneVerificationConfig } from '../../hooks/usePhoneVerification'
import { isRussianPhone } from '../../utils/phone'

export interface PhoneVerificationRefValue {
  sessionId: string
  statusToken: string
}

interface VerifyPhoneButtonProps {
  /** Canonical digit string, e.g. `79000000001` (same shape `PhoneInput` produces). */
  phone: string
  /** Fires with the redeemable session ref once verified, and with `null` the moment that stops being true (phone edited, dialog cancelled, session expired/rejected). */
  onVerifiedChange: (ref: PhoneVerificationRefValue | null) => void
  /** Label shown on the trigger button. */
  label?: string
  className?: string
}

/**
 * ARCHITECTURE_CYCLE14.md §148.1, T14-F4 — "сама решает, показываться ли": mounts unconditionally,
 * renders nothing when the subsystem is off/unhealthy or the phone isn't a valid Russian number yet
 * (§162 — "не показывается ничего: ни кнопки, ни объяснения, ни пустого места").
 */
export function VerifyPhoneButton({ phone, onVerifiedChange, label = 'Подтвердить номер через MAX', className = '' }: VerifyPhoneButtonProps) {
  const { data: config } = usePhoneVerificationConfig()
  const pv = usePhoneVerification(config?.pollIntervalSeconds ? config.pollIntervalSeconds * 1000 : undefined)
  const [dialogOpen, setDialogOpen] = useState(false)

  // US-14-05 (R8) — the session this button holds is only ever valid for the phone it was started
  // for; the moment the caller's own field changes, drop it.
  useEffect(() => {
    pv.syncPhone(phone)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phone])

  const verified = pv.status?.status === 'Verified'

  useEffect(() => {
    if (verified && pv.session) {
      onVerifiedChange({ sessionId: pv.session.sessionId, statusToken: pv.session.statusToken })
    } else {
      onVerifiedChange(null)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [verified, pv.session?.sessionId])

  if (!config?.enabled || !config.healthy) return null
  if (!isRussianPhone(phone)) return null

  if (verified) {
    return <PhoneVerifiedBadge verifiedAtUtc={pv.status?.verifiedAtUtc} className={className} />
  }

  const handleOpen = async () => {
    await pv.start(phone)
    setDialogOpen(true)
  }

  return (
    <div className={className}>
      <Button type="button" variant="secondary" size="sm" loading={pv.isStarting} onClick={handleOpen}>
        {label}
      </Button>
      {pv.startError && <p className="text-xs text-danger mt-1.5">{pv.startError}</p>}

      {dialogOpen && pv.session && (
        <VerifyPhoneDialog
          session={pv.session}
          status={pv.status}
          statusError={pv.statusError}
          onClose={() => setDialogOpen(false)}
          onRestart={() => void pv.start(phone)}
        />
      )}
    </div>
  )
}
