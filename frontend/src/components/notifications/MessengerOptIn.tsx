import { useId, useState } from 'react'
import { useMessengerLegalText } from '../../hooks/useMessengerLegalText'
import type { MessengerOptInState } from '../../hooks/useMessengerOptInDefault'
import type { CustomerMessagingOffer } from '../../types'
import {
  OPTED_OUT_LINK,
  OPTED_OUT_TEXT,
  OPT_IN_LESS,
  OPT_IN_MORE,
  OPT_IN_TEXT_KEY,
  fallbackCheckboxLabel,
  fallbackFullHtml,
  fallbackShortHtml,
  messengerNames,
  type OptInKind,
} from '../../utils/messengerOptIn'

interface Props {
  kind: OptInKind
  /** Публичный ответ сервера (`customerMessaging` / `customerNotifications` / `messenger`) в форме `CustomerMessagingOffer`. */
  offer: Pick<CustomerMessagingOffer, 'offered' | 'transports' | 'checkboxLabel'> | null | undefined
  state: MessengerOptInState
  /** Подставляется в `data-legal-value="companyName"` правового текста. */
  companyName?: string | null
  /** Куда ведёт «Изменить» у отписанного: профиль ezbook (в goods и dom — на ezbook.ru, пока нет US-40-13). */
  profileHref?: string | null
  className?: string
}

const LEGAL_CLASS =
  'legal-content text-xs leading-relaxed text-ink-soft [&_p]:mb-1.5 [&_p:last-child]:mb-0 [&_a]:underline [&_a]:text-gold-dark'

/**
 * Галочка клиента «Получать уведомления о {записи|заказе|брони} в {М}» (US-40-07/08, Т40-L-09/10). Отдельный элемент, не
 * совмещённый с кнопкой отправки; по умолчанию у гостя снята. Показывается только при `offer.offered` — решение сервера.
 * Подпись — `offer.checkboxLabel`; правовая строка под ней — ключ юриста либо запасной текст.
 */
export function MessengerOptIn({ kind, offer, state, companyName, profileHref, className = '' }: Props) {
  const legal = useMessengerLegalText(
    OPT_IN_TEXT_KEY[kind],
    { shortHtml: fallbackShortHtml(kind), fullHtml: fallbackFullHtml(kind) },
    companyName,
    !!offer?.offered && !state.optedOut,
  )
  const [expanded, setExpanded] = useState(false)
  const uid = useId()

  if (!offer?.offered) return null

  if (state.optedOut) {
    return (
      <p className={`text-sm text-ink-soft ${className}`} data-testid="messenger-opt-in-opted-out">
        {OPTED_OUT_TEXT}.
        {profileHref && (
          <>
            {' '}
            <a href={profileHref} className="font-medium text-gold-dark underline">
              {OPTED_OUT_LINK}
            </a>
          </>
        )}
      </p>
    )
  }

  const label = offer.checkboxLabel || fallbackCheckboxLabel(kind, messengerNames(offer.transports))
  const legalId = `${uid}-legal`

  return (
    <div className={`flex flex-col gap-1.5 ${className}`} data-testid="messenger-opt-in">
      <label className="flex min-h-[44px] cursor-pointer items-start gap-3 text-sm text-ink">
        <input
          type="checkbox"
          checked={state.checked}
          disabled={state.loading}
          onChange={(e) => state.setChecked(e.target.checked)}
          aria-describedby={legalId}
          className="mt-0.5 h-5 w-5 shrink-0 accent-gold"
        />
        <span>{label}</span>
      </label>
      <div id={legalId} className="pl-8">
        <div className={LEGAL_CLASS} dangerouslySetInnerHTML={{ __html: legal.shortHtml }} />
        {legal.fullHtml && (
          <>
            <button
              type="button"
              className="mt-1 inline-flex min-h-[32px] items-center text-xs font-semibold text-gold-dark hover:underline"
              aria-expanded={expanded}
              onClick={() => setExpanded((v) => !v)}
            >
              {expanded ? OPT_IN_LESS : OPT_IN_MORE}
            </button>
            {expanded && <div className={`mt-1.5 ${LEGAL_CLASS}`} dangerouslySetInnerHTML={{ __html: legal.fullHtml }} />}
          </>
        )}
      </div>
    </div>
  )
}
