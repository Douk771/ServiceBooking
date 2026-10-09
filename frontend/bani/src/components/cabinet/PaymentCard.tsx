import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { SavedNote, SectionCard, TextArea } from '@/components/slots/ui/formParts'
import { SlotNotice } from '@/components/slots/ui/SlotNotice'
import { getStayErrorMessage } from '@/utils/slots/slotError'
import { bathsCabinetApi } from '../../api/bathsCabinet'
import type { BathsCompanyManageDto } from '../../cabinet/types'

const DETAILS_MAX = 1000
const PURPOSE_MAX = 300

/** Requisites for the prepayment: shown only to the guest of a booking, never publicly (`BathPaymentRequisitesOwnerNotice`). */
export function PaymentCard({ company, onSaved }: { company: BathsCompanyManageDto; onSaved: () => void }) {
  const [details, setDetails] = useState(company.paymentDetails?.paymentDetails ?? '')
  const [purpose, setPurpose] = useState(company.paymentDetails?.paymentPurpose ?? '')
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')
  const needDetails = company.checklist.some((c) => c.code === 'PaymentDetails' && !c.done)

  const save = useMutation({
    mutationFn: () => bathsCabinetApi.updatePaymentDetails(company.id, { paymentDetails: details.trim() || null, paymentPurpose: purpose.trim() || null }),
    onSuccess: () => {
      setSaved(true)
      setError('')
      onSaved()
    },
    onError: (err) => setError(getStayErrorMessage(err, 'Не удалось сохранить реквизиты.')),
  })

  const tooLong = details.length > DETAILS_MAX ? `Реквизиты — не длиннее ${DETAILS_MAX} символов` : purpose.length > PURPOSE_MAX ? `Назначение платежа — не длиннее ${PURPOSE_MAX} символов` : ''

  return (
    <SectionCard id="payment-details" title="Реквизиты для оплаты" description="Куда гость переводит предоплату. Нужны, если у какой-то опубликованной бани предоплата больше нуля.">
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
          maxLength={DETAILS_MAX}
          value={details}
          onChange={(v) => {
            setDetails(v)
            setSaved(false)
          }}
          hint={
            <>
              {needDetails && !details.trim() && (
                <p className="text-xs text-warning" role="status">
                  Без реквизитов гости не смогут бронировать бани с предоплатой.
                </p>
              )}
              <SlotNotice textKey="BathPaymentRequisitesOwnerNotice" className="mt-1" />
            </>
          }
        />
        <Input
          label="Назначение платежа"
          maxLength={PURPOSE_MAX}
          value={purpose}
          onChange={(e) => {
            setPurpose(e.target.value)
            setSaved(false)
          }}
        />
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
