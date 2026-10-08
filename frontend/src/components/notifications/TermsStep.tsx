import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { notificationNumbersApi, type NumberChannelDto, type NumbersOverviewDto, type TransportNumbersDto } from '../../api/notificationNumbers'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import { Input } from '../ui/Input'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import { isPlausibleInn } from '../../utils/inn'
import type { components } from '../../types/api-cycle40.generated'
import * as T from './numbersTexts'

type LegalEntityForm = components['schemas']['LegalEntityForm']

interface Props {
  transport: TransportNumbersDto
  overview: Pick<NumbersOverviewDto, 'offer' | 'risk' | 'statusNotice'>
  /** true — шаг «Оплата» (заявка на оплату), false — шаг «Условия» (триал/оплачено, без заявки; Т40-L-03). */
  paymentRequest: boolean
  onSubmitted: (channel: NumberChannelDto) => void
  onCancel: () => void
}

/**
 * Шаги «Оплата» и «Условия» — одна форма: статус, ИНН, две раздельные отметки (оферта и риск, Т40-L-02), полный текст риска
 * до отметки. Кнопка неактивна без обеих отметок. Версии оферты и риска берутся из overview, не из захардкоженных строк.
 */
export function TermsStep({ transport, overview, paymentRequest, onSubmitted, onCancel }: Props) {
  const [form, setForm] = useState<LegalEntityForm>(transport.prefill?.legalEntityForm ?? 'Ip')
  const [inn, setInn] = useState(transport.prefill?.inn ?? '')
  const [offerChecked, setOfferChecked] = useState(false)
  const [riskChecked, setRiskChecked] = useState(false)

  const innValid = isPlausibleInn(inn)
  const mut = useMutation({
    mutationFn: () =>
      notificationNumbersApi.request({
        legalEntityForm: form,
        inn: inn.replace(/\D/g, ''),
        offerAccepted: { version: overview.offer.version },
        riskAccepted: { version: overview.risk.version },
        transport: transport.transport,
        paymentRequest,
      }),
    onSuccess: onSubmitted,
  })

  const m = transport.displayName
  return (
    <div className="flex flex-col gap-4">
      {!paymentRequest && transport.isTrial && <p className="text-sm text-ink-soft">{T.TERMS_TRIAL_HINT(m)}</p>}
      <p className="text-sm text-ink-soft">{overview.statusNotice}</p>

      {transport.connectionNotice && (
        <p className="text-xs text-ink-soft bg-cream-deep rounded-xl px-3.5 py-2.5 flex items-start gap-2">
          <Icon name="alert-circle" size={14} strokeWidth={1.8} className="shrink-0 mt-0.5 text-muted" />
          <span>{transport.connectionNotice}</span>
        </p>
      )}

      <div className="flex flex-col gap-1.5">
        <label htmlFor="numbers-legal-form" className="text-[13px] font-medium text-[#4A4038]">
          Статус
        </label>
        <select
          id="numbers-legal-form"
          value={form}
          onChange={(e) => setForm(e.target.value as LegalEntityForm)}
          className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold bg-white text-ink"
        >
          {(Object.keys(T.FORM_LABELS) as LegalEntityForm[]).map((v) => (
            <option key={v} value={v}>
              {T.FORM_LABELS[v]}
            </option>
          ))}
        </select>
      </div>

      <div>
        <Input
          label="ИНН"
          placeholder="10 или 12 цифр"
          inputMode="numeric"
          value={inn}
          onChange={(e) => setInn(e.target.value)}
          error={inn && !innValid ? T.INN_INVALID : undefined}
        />
        <p className="text-xs text-muted mt-1.5">{T.INN_HINT}</p>
      </div>

      <div className="flex flex-col gap-1.5">
        <p className="text-[13px] font-medium text-[#4A4038]">{T.RISK_BOX_LABEL}</p>
        <div
          tabIndex={0}
          role="region"
          aria-label={T.RISK_BOX_LABEL}
          className="max-h-48 overflow-y-auto rounded-xl border border-line bg-white px-4 py-3 text-xs text-ink-soft leading-relaxed [&_h2]:font-semibold [&_h2]:text-ink [&_h2]:mt-3 [&_p]:mb-2"
          dangerouslySetInnerHTML={{ __html: overview.risk.html }}
        />
        <Link to={overview.risk.url} target="_blank" className="text-xs text-gold hover:text-gold-dark self-start">
          Открыть отдельной страницей
        </Link>
      </div>

      <label className="flex items-start gap-2.5 cursor-pointer">
        <input type="checkbox" className="w-4 h-4 mt-0.5 rounded accent-gold" checked={offerChecked} onChange={(e) => setOfferChecked(e.target.checked)} />
        <span className="text-sm text-ink-soft">
          {T.OFFER_CHECKBOX_PREFIX}{' '}
          <Link to={overview.offer.url} target="_blank" className="text-gold hover:text-gold-dark">
            {T.OFFER_LINK_LABEL}
          </Link>{' '}
          {T.OFFER_CHECKBOX_SUFFIX}
        </span>
      </label>
      <label className="flex items-start gap-2.5 cursor-pointer">
        <input type="checkbox" className="w-4 h-4 mt-0.5 rounded accent-gold" checked={riskChecked} onChange={(e) => setRiskChecked(e.target.checked)} />
        <span className="text-sm text-ink-soft">{T.RISK_CHECKBOX}</span>
      </label>

      {mut.isError && (
        <p role="alert" className="text-sm text-danger">
          {getNotificationErrorMessage(mut.error)}
        </p>
      )}

      <div className="flex gap-3 pt-1">
        <Button variant="secondary" className="flex-1" onClick={onCancel}>
          {T.CANCEL}
        </Button>
        <Button className="flex-1" disabled={!innValid || !offerChecked || !riskChecked} loading={mut.isPending} onClick={() => mut.mutate()}>
          {paymentRequest ? T.TERMS_SUBMIT_PAYMENT : T.TERMS_SUBMIT_TERMS}
        </Button>
      </div>
    </div>
  )
}
