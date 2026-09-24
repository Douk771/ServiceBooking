import { Link } from 'react-router-dom'
import { useLegalText } from '../../hooks/useLegalText'
import { Icon } from '../ui/Icon'

/**
 * API_CONTRACT_CYCLE16.md §276, ARCHITECTURE_CYCLE16.md §245.6 п.2 (TD-03). Shown on `/profile`
 * next to "Скачать мои данные" and on `/profile/delete-account` before the user confirms deletion.
 *
 * 🔴 The ONLY allowed trigger is `profile.phoneVerified === false` (§276.1/§272) — never "the export
 * came back empty" or any other inference from response contents. Passing anything else here would
 * turn this component into the oracle the contract explicitly forbids.
 *
 * Text is never hardcoded here (§276.3): it comes from `GET /api/legal/texts/GuestDataGateNotice`
 * (`useLegalText`, same mechanism as `PublicAddressNotice`), or — until legal-counsel publishes that
 * key — a neutral fallback matching ARCHITECTURE_CYCLE16.md §245.6 п.3 verbatim, so the screen is
 * never empty while the key is missing.
 */
const FALLBACK_TEXT =
  'Эти сведения доступны после подтверждения номера телефона. Если подтвердить номер невозможно, ' +
  'направьте обращение субъекта персональных данных — ответ по закону даётся в установленный срок.'

interface Props {
  /** Only condition under which this component should even be mounted — enforced by callers, not
   *  re-derived here, so there is exactly one place in the codebase making this decision. */
  phoneVerified: boolean
  className?: string
}

export function GuestDataGateNotice({ phoneVerified, className }: Props) {
  const { data: text } = useLegalText('GuestDataGateNotice')

  if (phoneVerified) return null

  return (
    <div
      className={`bg-info-bg text-info text-sm px-4 py-3 rounded-xl flex items-start gap-2 ${className ?? ''}`}
    >
      <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5" />
      <div className="flex flex-col gap-1.5">
        {text?.contentHtml ? (
          <div
            className="[&_p]:mb-1.5 [&_p:last-child]:mb-0 [&_a]:underline"
            dangerouslySetInnerHTML={{ __html: text.contentHtml }}
          />
        ) : (
          <p>{FALLBACK_TEXT}</p>
        )}
        <Link to="/subject-request" className="font-semibold underline hover:no-underline w-fit">
          Оставить обращение
        </Link>
      </div>
    </div>
  )
}
