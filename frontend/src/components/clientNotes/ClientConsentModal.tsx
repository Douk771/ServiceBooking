import { useLegalText } from '../../hooks/useLegalText'
import { findSection, splitLegalSections } from '../../utils/legalSections'
import { Modal } from '../ui/Modal'
import { Button } from '../ui/Button'
import type { LegalTextKey } from '../../types'

interface Props {
  textKey: Extract<LegalTextKey, 'PhotoConsent' | 'HealthDataConsent'>
  title: string
  loading: boolean
  error?: string
  onConfirm: (textVersion: string) => void
  onClose: () => void
}

/**
 * Shared shape of D10 (PhotoConsent) and D11 (HealthDataConsent): shown to the CLIENT by a staff
 * member, who then attests that consent was obtained (§44.2, §45.4) — the staff member is the one
 * clicking through this UI, the client only saw the text on the screen. Both documents structure the
 * client-facing text under the same heading, so one component covers both (T5-F5, T5-F6).
 */
export function ClientConsentModal({ textKey, title, loading, error, onConfirm, onClose }: Props) {
  const { data: text, isLoading } = useLegalText(textKey)
  const sections = text ? splitLegalSections(text.contentHtml) : []
  const clientText = findSection(sections, 'Текст для клиента')

  return (
    <Modal title={title} onClose={onClose}>
      <div className="flex flex-col gap-4">
        {isLoading ? (
          <div className="h-32 bg-cream-deep rounded-xl animate-pulse" />
        ) : clientText || text ? (
          <div
            className="legal-content text-sm text-ink-soft [&_p]:mb-2 [&_strong]:text-ink [&_a]:text-gold [&_a]:hover:text-gold-dark"
            dangerouslySetInnerHTML={{ __html: (clientText ?? { html: text!.contentHtml }).html }}
          />
        ) : (
          <p className="text-sm text-danger">Не удалось загрузить текст согласия.</p>
        )}

        {error && <p className="text-sm text-danger">{error}</p>}

        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Не сейчас
          </Button>
          <Button className="flex-1" disabled={!text} loading={loading} onClick={() => text && onConfirm(text.version)}>
            Клиент согласен
          </Button>
        </div>
      </div>
    </Modal>
  )
}
