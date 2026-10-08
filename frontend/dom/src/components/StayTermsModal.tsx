import { Modal } from '@/components/ui/Modal'
import { applyLegalRuntimeValues } from '@/utils/legalRuntimeValues'
import { useStayText } from '../hooks/useStayText'

/** «Условия бронирования и проживания» (`StayBookingTerms`) — the guest's contract with the company; opens from the house and booking pages. */
export function StayTermsModal({ companyName, onClose }: { companyName?: string | null; onClose: () => void }) {
  const text = useStayText('StayBookingTerms')
  const cancellation = useStayText('StayCancellationTerms')
  const render = (html: string) => applyLegalRuntimeValues(html, { companyName: companyName ?? null })
  return (
    <Modal title="Условия бронирования и проживания" onClose={onClose}>
      <div
        id="stay-terms"
        className="legal-content flex flex-col gap-2 text-sm leading-relaxed text-ink-soft [&_h3]:hidden [&_p]:mb-2 [&_a]:text-gold-dark [&_a]:underline"
        dangerouslySetInnerHTML={{ __html: render(text.short) }}
      />
      <h3 className="mb-1 mt-5 text-sm font-semibold text-ink">Правила отмены</h3>
      <div
        className="text-sm leading-relaxed text-ink-soft [&_p]:mb-2"
        dangerouslySetInnerHTML={{ __html: render(`${cancellation.full ?? ''}${cancellation.short}`) }}
      />
    </Modal>
  )
}
