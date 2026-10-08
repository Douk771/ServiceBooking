import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { formatPhone, telHref } from '@/utils/phone'
import type { PublicStayBookingDto } from '../types'
import { InlineError } from './StatePanels'

/**
 * Confirmation before the guest cancels (API_CONTRACT_CYCLE37.md §37.26.4, ЮР-1): the server's refund text — «К возврату не меньше X ₽…»
 * (counted on the server's time, so the dialog always works from a freshly fetched booking) — and the line that the REFUND IS MADE BY
 * THE COMPANY, with its phone. The wording of the amount is never composed here.
 */
export function CancelBookingDialog({
  booking,
  pending,
  error,
  onConfirm,
  onClose,
}: {
  booking: PublicStayBookingDto
  pending: boolean
  error: string
  onConfirm: () => void
  onClose: () => void
}) {
  const { refund, summary } = booking.cancellation
  const phone = booking.company.phone
  return (
    <Modal title="Отменить бронь?" onClose={onClose} dismissible={!pending}>
      <div className="flex flex-col gap-3 text-sm text-ink-soft">
        <p className="rounded-xl bg-cream-deep px-4 py-3 font-medium text-ink" data-testid="refund-text">
          {refund.text}
        </p>
        {refund.kind !== 'NothingPaid' && (
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
        <p className="text-xs text-muted">{summary}</p>
        {error && <InlineError>{error}</InlineError>}
      </div>
      <div className="mt-5 flex gap-3">
        <Button variant="secondary" className="min-h-[44px] flex-1" onClick={onClose} disabled={pending}>
          Оставить бронь
        </Button>
        <Button variant="danger" className="min-h-[44px] flex-1" loading={pending} onClick={onConfirm}>
          Отменить бронь
        </Button>
      </div>
    </Modal>
  )
}
