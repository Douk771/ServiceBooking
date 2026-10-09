import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { formatPhone, telHref } from '@/utils/phone'
import { InlineError } from '../StatePanels'

/**
 * Confirmation before the guest cancels a session (API_CONTRACT_CYCLE39.md §39.23.4, §39.24.5, ЮР39-1). The server's refund text is
 * shown AS IS — it is never rewritten and no amount is composed here («не меньше 0 ₽» does not exist: at a zero remainder the
 * server says «только фактические расходы, не больше N ₽»). When money was paid, the dialog says the REFUND IS MADE BY THE COMPANY and
 * gives its phone. `refundText` is null for a session in a booking: nothing was paid, the cancellation has no consequences.
 */
export function CancelSessionDialog({
  title = 'Отменить сеанс?',
  refundText,
  refundMadeByCompany,
  summary,
  phone,
  pending,
  error,
  confirmLabel = 'Отменить сеанс',
  keepLabel = 'Оставить сеанс',
  onConfirm,
  onClose,
}: {
  title?: string
  refundText: string | null
  refundMadeByCompany: boolean
  summary?: string | null
  phone?: string | null
  pending: boolean
  error: string
  confirmLabel?: string
  keepLabel?: string
  onConfirm: () => void
  onClose: () => void
}) {
  return (
    <Modal title={title} onClose={onClose} dismissible={!pending}>
      <div className="flex flex-col gap-3 text-sm text-ink-soft">
        {refundText && (
          <p className="rounded-xl bg-cream-deep px-4 py-3 font-medium text-ink" data-testid="refund-text">
            {refundText}
          </p>
        )}
        {refundMadeByCompany && (
          <p>
            Возврат делает компания, а не сервис
            {phone ? (
              <>
                {' '}
                — свяжитесь с ней:{' '}
                <a href={telHref(phone) || undefined} className="font-semibold !text-ink">
                  {formatPhone(phone)}
                </a>
              </>
            ) : null}
            .
          </p>
        )}
        {summary && <p className="text-xs text-muted">{summary}</p>}
        {error && <InlineError>{error}</InlineError>}
      </div>
      <div className="mt-5 flex gap-3">
        <Button variant="secondary" className="min-h-[44px] flex-1" onClick={onClose} disabled={pending}>
          {keepLabel}
        </Button>
        <Button variant="danger" className="min-h-[44px] flex-1" loading={pending} onClick={onConfirm}>
          {confirmLabel}
        </Button>
      </div>
    </Modal>
  )
}
