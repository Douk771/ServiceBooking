import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { notificationChannelsApi } from '../../api/notificationChannels'
import { legalApi } from '../../api/legal'
import { Modal } from '../ui/Modal'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import { isPlausibleInn } from '../../utils/inn'
import type { LegalEntityForm } from '../../types'

const FORM_LABELS: Record<LegalEntityForm, string> = {
  Ip: 'Индивидуальный предприниматель',
  Company: 'Юридическое лицо',
  SelfEmployed: 'Самозанятый (плательщик НПД)',
}

/**
 * API_CONTRACT_CYCLE5.md §50.1 (BREAKING № 7), US-82. The channel offer (D9) lives as an appendix to
 * `TermsOwner` (§43.2) rather than as its own document type, so `offerAccepted.version` is pinned to
 * the current `TermsOwner` version, the same one the owner accepted to create the company.
 */
export function ChannelRequestModal({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient()
  const [legalEntityForm, setLegalEntityForm] = useState<LegalEntityForm>('Ip')
  const [inn, setInn] = useState('')
  const [offerAccepted, setOfferAccepted] = useState(false)

  const { data: manifest } = useQuery({ queryKey: ['legal-documents'], queryFn: legalApi.getManifest })
  const ownerTerms = manifest?.documents.find((d) => d.type === 'TermsOwner')

  const mut = useMutation({
    mutationFn: () => {
      if (!ownerTerms) throw new Error('TermsOwner version not loaded')
      return notificationChannelsApi.request({ legalEntityForm, inn, offerAccepted: { version: ownerTerms.version } })
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['notification-channels'] })
      onClose()
    },
  })

  const innValid = isPlausibleInn(inn)

  return (
    <Modal title="Подключить канал уведомлений" onClose={onClose}>
      <div className="flex flex-col gap-4">
        <p className="text-sm text-ink-soft">
          Платные функции доступны только тем, кто ведёт предпринимательскую деятельность (US-82). Бесплатная часть
          сервиса статуса не требует.
        </p>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="channel-legal-form" className="text-[13px] font-medium text-[#4A4038]">
            Форма
          </label>
          <select
            id="channel-legal-form"
            value={legalEntityForm}
            onChange={(e) => setLegalEntityForm(e.target.value as LegalEntityForm)}
            className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold bg-white text-ink"
          >
            {Object.entries(FORM_LABELS).map(([v, l]) => (
              <option key={v} value={v}>
                {l}
              </option>
            ))}
          </select>
        </div>

        <Input
          label="ИНН"
          placeholder="10 или 12 цифр"
          value={inn}
          onChange={(e) => setInn(e.target.value)}
          error={inn && !innValid ? 'ИНН должен содержать 10 или 12 цифр' : undefined}
        />
        <p className="text-xs text-muted -mt-2.5">ИНН нужен только для счёта и виден вам и суперадмину.</p>

        <label className="flex items-start gap-2.5 cursor-pointer">
          <input
            type="checkbox"
            className="w-4 h-4 mt-0.5 rounded accent-gold"
            checked={offerAccepted}
            onChange={(e) => setOfferAccepted(e.target.checked)}
          />
          <span className="text-sm text-ink-soft">
            Я ознакомлен(а) с{' '}
            <Link to="/offer-channel" target="_blank" className="text-gold hover:text-gold-dark">
              офертой на подключение канала уведомлений
            </Link>{' '}
            и действую в предпринимательских целях
          </span>
        </label>

        {mut.isError && <p className="text-sm text-danger">{getNotificationErrorMessage(mut.error)}</p>}

        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button
            className="flex-1"
            disabled={!innValid || !offerAccepted || !ownerTerms}
            loading={mut.isPending}
            onClick={() => mut.mutate()}
          >
            Подать заявку
          </Button>
        </div>
      </div>
    </Modal>
  )
}
