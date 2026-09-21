import { useState } from 'react'
import { Modal } from '../../components/ui/Modal'
import { Button } from '../../components/ui/Button'
import { Icon } from '../../components/ui/Icon'

interface Props {
  confirmationHtml: string | null
  heightenedHtml: string | null
  markersHit: string[]
  loading: boolean
  error?: string
  onConfirm: (confirmedDespiteMarkers: boolean) => void
  onClose: () => void
}

/**
 * API_CONTRACT_CYCLE5.md §47.2 (BREAKING № 6), §47.3; ARCHITECTURE_CYCLE5.md T5-F7. Shown on EVERY
 * save (US-69 п. 2 — never cached, never inherited from the previous save), regardless of whether the
 * ad-marker dictionary hit anything. When it did, a SECOND, separate checkbox is required
 * (`confirmedDespiteMarkers`) — the base confirmation alone does not cover it.
 */
export function TemplateAcknowledgementModal({
  confirmationHtml,
  heightenedHtml,
  markersHit,
  loading,
  error,
  onConfirm,
  onClose,
}: Props) {
  const [accepted, setAccepted] = useState(false)
  const [confirmedDespiteMarkers, setConfirmedDespiteMarkers] = useState(false)
  const hasMarkers = markersHit.length > 0
  const canConfirm = accepted && (!hasMarkers || confirmedDespiteMarkers)

  return (
    <Modal title="Подтвердите сохранение шаблона" onClose={onClose}>
      <div className="flex flex-col gap-4">
        {hasMarkers && (
          <div className="rounded-xl bg-warning-bg text-warning text-sm px-4 py-3 flex flex-col gap-2">
            <p className="font-semibold flex items-center gap-1.5">
              <Icon name="alert-circle" size={15} strokeWidth={1.8} /> Похоже, в тексте есть реклама
            </p>
            {heightenedHtml ? (
              <div
                className="legal-content [&_p]:mb-1.5 last:[&_p]:mb-0"
                dangerouslySetInnerHTML={{ __html: heightenedHtml }}
              />
            ) : (
              <p>
                Мы нашли в тексте слова, которые обычно встречаются в рекламных рассылках: {markersHit.join(', ')}.
                Реклама через этот канал запрещена законом — штраф для организации от 100 000 ₽ за каждый выявленный
                факт.
              </p>
            )}
            <label className="flex items-start gap-2.5 cursor-pointer mt-1">
              <input
                type="checkbox"
                className="w-4 h-4 mt-0.5 rounded accent-gold"
                checked={confirmedDespiteMarkers}
                onChange={(e) => setConfirmedDespiteMarkers(e.target.checked)}
              />
              <span className="text-sm">Всё равно сохранить, несмотря на найденные маркеры</span>
            </label>
          </div>
        )}

        <div className="text-sm text-ink-soft">
          {confirmationHtml ? (
            <div className="legal-content [&_p]:mb-1.5 last:[&_p]:mb-0" dangerouslySetInnerHTML={{ __html: confirmationHtml }} />
          ) : (
            <p>Сохраняя шаблон, я подтверждаю, что текст сообщения носит сервисный характер и не содержит рекламы.</p>
          )}
        </div>

        <label className="flex items-start gap-2.5 cursor-pointer">
          <input
            type="checkbox"
            className="w-4 h-4 mt-0.5 rounded accent-gold"
            checked={accepted}
            onChange={(e) => setAccepted(e.target.checked)}
          />
          <span className="text-sm text-ink-soft">Подтверждаю</span>
        </label>

        {error && <p className="text-sm text-danger">{error}</p>}

        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button className="flex-1" disabled={!canConfirm} loading={loading} onClick={() => onConfirm(confirmedDespiteMarkers)}>
            Сохранить
          </Button>
        </div>
      </div>
    </Modal>
  )
}
