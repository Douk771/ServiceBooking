import { format } from 'date-fns'
import { ru } from 'date-fns/locale'
import { Icon } from '../ui/Icon'
import { Button } from '../ui/Button'
import { getFailureReasonFallback, isRetryableFailure } from '../../utils/phoneVerificationError'
import type { PhoneVerificationSessionStatus } from '../../types'

interface VerificationStatusProps {
  status: PhoneVerificationSessionStatus | null
  /** Offered on Rejected/Expired for reasons where trying again makes sense (§165, US-14-06). Omit to hide the action entirely (e.g. read-only contexts). */
  onRestart?: () => void
  /** Set when the status poll itself failed (e.g. session 404'd server-side). Takes priority over
   * `status` — there's nothing meaningful left to show about a session the server no longer knows. */
  statusError?: string | null
}

/**
 * ARCHITECTURE_CYCLE14.md §148.4 — announced to assistive tech via `aria-live="polite"`, not only a
 * colour change. `message` is always the server's own text (§164: "фронт её не сочиняет"); this
 * component never assembles wording from `failureReason` itself except as a last-resort fallback if
 * the server ever sends a reason without a message.
 */
export function VerificationStatus({ status, onRestart, statusError }: VerificationStatusProps) {
  if (statusError) {
    return (
      <div role="status" aria-live="polite">
        <div className="flex flex-col gap-2">
          <p className="text-sm text-danger flex items-start gap-2">
            <Icon name="alert-circle" size={16} strokeWidth={1.8} className="shrink-0 mt-0.5" />
            {statusError}
          </p>
          {onRestart && (
            <Button type="button" variant="secondary" size="sm" onClick={onRestart} className="self-start">
              Получить новую ссылку
            </Button>
          )}
        </div>
      </div>
    )
  }

  if (!status) return null

  // §165 — "SessionCancelled: ничего, экран уже не показывает сессию". Cancellation here is always
  // something WE did (the user closed the dialog or edited the phone number); the caller is expected
  // to have already stopped rendering this session, but render nothing defensively either way.
  if (status.status === 'Cancelled') return null

  let body: JSX.Element
  switch (status.status) {
    case 'Pending':
      body = (
        <p className="text-sm text-ink-soft flex items-center gap-2">
          <Spinner /> Откройте ссылку или отсканируйте QR-код, чтобы открыть бота MAX
        </p>
      )
      break
    case 'Linked':
      body = (
        <p className="text-sm text-ink-soft flex items-center gap-2">
          <Spinner /> Бот открыт. Поделитесь своим контактом, когда бот попросит
        </p>
      )
      break
    case 'Verified': {
      const dateLabel = status.verifiedAtUtc ? format(new Date(status.verifiedAtUtc), 'd MMM yyyy, HH:mm', { locale: ru }) : null
      body = (
        <p className="text-sm text-success flex items-center gap-2">
          <Icon name="check-circle" size={16} strokeWidth={1.8} />
          Номер подтверждён{dateLabel ? ` · ${dateLabel}` : ''}
        </p>
      )
      break
    }
    case 'Rejected':
    case 'Expired': {
      const text =
        status.message ||
        getFailureReasonFallback(status.failureReason) ||
        (status.status === 'Expired' ? 'Ссылка устарела. Получите новую' : 'Не удалось подтвердить номер')
      const canRetry = status.status === 'Expired' || isRetryableFailure(status.failureReason)
      body = (
        <div className="flex flex-col gap-2">
          <p className="text-sm text-danger flex items-start gap-2">
            <Icon name="alert-circle" size={16} strokeWidth={1.8} className="shrink-0 mt-0.5" />
            {text}
          </p>
          {canRetry && onRestart && (
            <Button type="button" variant="secondary" size="sm" onClick={onRestart} className="self-start">
              Получить новую ссылку
            </Button>
          )}
        </div>
      )
      break
    }
    default:
      body = <></>
  }

  return (
    <div role="status" aria-live="polite">
      {body}
    </div>
  )
}

function Spinner() {
  return (
    <svg className="animate-spin h-3.5 w-3.5 text-muted shrink-0" fill="none" viewBox="0 0 24 24" aria-hidden="true">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
    </svg>
  )
}
