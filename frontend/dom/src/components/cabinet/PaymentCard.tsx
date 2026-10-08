import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { staysCompaniesApi } from '../../api/staysCompanies'
import type { StaysCompanyManageDto } from '../../types'
import { getStayErrorMessage } from '../../utils/stayError'
import { StayNotice } from '../StayNotice'
import { SavedNote, SectionCard, TextArea } from './formParts'

/** Requisites for the prepayment (Т37-04: shown only to the guest of a booking, never publicly) + the purpose of payment. */
export function PaymentCard({ company, onSaved }: { company: StaysCompanyManageDto; onSaved: () => void }) {
  const [details, setDetails] = useState(company.paymentDetails?.paymentDetails ?? '')
  const [purpose, setPurpose] = useState(company.paymentDetails?.paymentPurpose ?? '')
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')
  const prepay = company.settings?.prepayPercent ?? 0

  const save = useMutation({
    mutationFn: () => staysCompaniesApi.updatePaymentDetails(company.id, { paymentDetails: details.trim() || null, paymentPurpose: purpose.trim() || null }),
    onSuccess: () => {
      setSaved(true)
      setError('')
      onSaved()
    },
    onError: (err) => setError(getStayErrorMessage(err, 'Не удалось сохранить реквизиты.')),
  })

  const tooLong = details.length > 1000 ? 'Реквизиты — не длиннее 1000 символов' : purpose.length > 200 ? 'Назначение платежа — не длиннее 200 символов' : ''

  return (
    <SectionCard title="Реквизиты для оплаты" description="Куда гость переводит предоплату.">
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault()
          setSaved(false)
          if (tooLong) {
            setError(tooLong)
            return
          }
          setError('')
          save.mutate()
        }}
      >
        <TextArea
          label="Реквизиты"
          rows={4}
          maxLength={1000}
          value={details}
          onChange={(v) => {
            setDetails(v)
            setSaved(false)
          }}
          hint={
            <>
              {prepay > 0 && !details.trim() && (
                <p className="text-xs text-warning" role="status">
                  Без реквизитов гости не смогут бронировать с предоплатой.
                </p>
              )}
              <StayNotice textKey="StayPaymentRequisitesOwnerNotice" className="mt-1" />
            </>
          }
        />
        <Input label="Назначение платежа" maxLength={200} value={purpose} onChange={(e) => { setPurpose(e.target.value); setSaved(false) }} />
        {error && <InlineError>{error}</InlineError>}
        <div className="flex items-center gap-3">
          <Button type="submit" loading={save.isPending} className="min-h-[44px]">
            Сохранить реквизиты
          </Button>
          <SavedNote show={saved} />
        </div>
      </form>
    </SectionCard>
  )
}
