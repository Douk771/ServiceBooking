import { Modal } from '@/components/ui/Modal'
import { applyLegalRuntimeValues } from '@/utils/legalRuntimeValues'
import { useStayText } from '../../hooks/useStayText'

/** «Условия оказания услуги» (`StayServiceBookingTerms`) and the cancellation rules of a session (`StayServiceCancellationTerms`). */
export function ServiceTermsModal({ companyName, onClose }: { companyName?: string | null; onClose: () => void }) {
  const terms = useStayText('StayServiceBookingTerms')
  const cancellation = useStayText('StayServiceCancellationTerms')
  const render = (html: string) => applyLegalRuntimeValues(html, { companyName: companyName ?? null })
  return (
    <Modal title="Условия оказания услуги" onClose={onClose}>
      <div
        id="service-terms"
        className="flex flex-col gap-2 text-sm leading-relaxed text-ink-soft [&_h3]:hidden [&_p]:mb-2 [&_a]:text-gold-dark [&_a]:underline"
        dangerouslySetInnerHTML={{ __html: render(terms.short) }}
      />
      <h3 className="mb-1 mt-5 text-sm font-semibold text-ink">Правила отмены</h3>
      <div
        className="text-sm leading-relaxed text-ink-soft [&_p]:mb-2"
        dangerouslySetInnerHTML={{ __html: render(`${cancellation.full ?? ''}${cancellation.short}`) }}
      />
    </Modal>
  )
}
