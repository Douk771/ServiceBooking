import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useLegalText } from '../../hooks/useLegalText'
import { ownerFacingSection } from '../../utils/legalSection'
import { companyAddressApi } from '../../api/companyAddress'
import { Modal } from '../ui/Modal'
import { Button } from '../ui/Button'

interface Props {
  /** Called once `POST /api/companies/address/notice` has succeeded. The caller is then free to
   *  proceed with actually saving the address — this component never saves anything itself. */
  onConfirmed: () => void
  onCancel: () => void
}

/**
 * ARCHITECTURE_CYCLE13.md §220.2 (US by LEGAL_REVIEW.md §16.4). Shown before saving the address, on
 * first fill AND on every edit, on both screens where an address is entered — independent of the
 * geocoder switch (`AddressVerification:Provider`), which this has nothing to do with.
 *
 * Text and version come from `GET /api/legal/texts/PublicAddressNotice` (`useLegalText`, 5 min cache)
 * — never hardcoded here, per §220.2/R24. Only the "Текст для владельца" section is shown
 * (`ownerFacingSection`); when the anchor heading isn't found, confirmation is disabled rather than
 * showing the wrong section or inventing wording.
 */
export function PublicAddressNotice({ onConfirmed, onCancel }: Props) {
  const { data: text, isLoading, refetch } = useLegalText('PublicAddressNotice')
  const [error, setError] = useState('')

  const confirmMut = useMutation({
    mutationFn: (textVersion: string) => companyAddressApi.confirmNotice(textVersion),
    onSuccess: () => {
      setError('')
      onConfirmed()
    },
    onError: async (err: unknown) => {
      const status = (err as { response?: { status?: number } })?.response?.status
      if (status === 409) {
        // §242 — the text changed under the reader; re-read it and ask them to confirm again rather
        // than silently recording acceptance of a version they never saw.
        setError('Текст обновился, пока вы его читали. Прочитайте заново и подтвердите ещё раз.')
        await refetch()
      } else if (status === 503) {
        setError('Правовые документы временно недоступны.')
      } else {
        setError('Не удалось сохранить подтверждение. Попробуйте ещё раз.')
      }
    },
  })

  const section = text ? ownerFacingSection(text.contentHtml) : null

  return (
    <Modal title="Адрес станет общедоступным" onClose={onCancel}>
      <div className="flex flex-col gap-4">
        {isLoading ? (
          <div className="h-32 bg-cream-deep rounded-xl animate-pulse" />
        ) : section ? (
          <div
            className="legal-content text-sm text-ink-soft [&_p]:mb-2 [&_strong]:text-ink"
            dangerouslySetInnerHTML={{ __html: section }}
          />
        ) : (
          <p className="text-sm text-danger">Текст предупреждения временно недоступен.</p>
        )}

        {error && <p className="text-sm text-danger">{error}</p>}

        <div className="flex gap-3 pt-1">
          <Button type="button" variant="secondary" className="flex-1" onClick={onCancel}>
            Отмена
          </Button>
          <Button
            type="button"
            className="flex-1"
            disabled={!section || !text}
            loading={confirmMut.isPending}
            onClick={() => text && confirmMut.mutate(text.version)}
          >
            Понятно, сохранить адрес
          </Button>
        </div>
      </div>
    </Modal>
  )
}
