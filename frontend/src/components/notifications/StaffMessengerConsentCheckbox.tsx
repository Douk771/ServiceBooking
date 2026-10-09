import { useId } from 'react'
import { useMessengerLegalText } from '../../hooks/useMessengerLegalText'
import type { CustomerMessagingOffer } from '../../types'
import { messengerNames, staffConsentLabel, staffHintFallbackHtml } from '../../utils/messengerOptIn'

interface Props {
  offer: Pick<CustomerMessagingOffer, 'offered' | 'transports'> | null | undefined
  checked: boolean
  onChange: (v: boolean) => void
}

/**
 * Отметка сотрудника в ручной записи ezbook (US-40-08, Р40-Ю2, Т40-L-10): «Клиент согласился получать сообщения об этой
 * записи в {М}». Всегда по умолчанию снята и никогда не предзаполняется; подсказка — ключ `StaffBookingMessengerConsentHint`
 * (запасной текст — LEGAL_REVIEW_CYCLE40.md §7.3). Без отметки запись создаётся так же — поле не отправляется.
 */
export function StaffMessengerConsentCheckbox({ offer, checked, onChange }: Props) {
  const messenger = messengerNames(offer?.transports ?? [])
  const hint = useMessengerLegalText('StaffBookingMessengerConsentHint', { shortHtml: staffHintFallbackHtml(messenger), fullHtml: null }, null, !!offer?.offered)
  const uid = useId()
  if (!offer?.offered) return null

  const hintId = `${uid}-hint`
  return (
    <div className="flex flex-col gap-1.5" data-testid="staff-messenger-consent">
      <label className="flex min-h-[44px] cursor-pointer items-start gap-3 text-[13px] text-ink">
        <input
          type="checkbox"
          checked={checked}
          onChange={(e) => onChange(e.target.checked)}
          aria-describedby={hintId}
          className="mt-0.5 h-5 w-5 shrink-0 accent-gold"
        />
        <span>{staffConsentLabel(messenger)}</span>
      </label>
      <div
        id={hintId}
        className="legal-content pl-8 text-xs leading-relaxed text-ink-soft [&_p]:mb-1.5 [&_p:last-child]:mb-0"
        dangerouslySetInnerHTML={{ __html: hint.shortHtml }}
      />
    </div>
  )
}
