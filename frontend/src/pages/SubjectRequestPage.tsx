import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { subjectRequestsApi } from '../api/subjectRequests'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { Icon } from '../components/ui/Icon'
import { SmartCaptcha, smartCaptchaEnabled } from '../components/booking/SmartCaptcha'
import { getSubjectRequestErrorMessage } from '../utils/subjectRequestError'
import type { SubjectRequestKind } from '../types'

const KIND_LABELS: Record<SubjectRequestKind, string> = {
  Access: 'Узнать, какие данные обо мне есть',
  Rectification: 'Исправить неточные данные',
  Erasure: 'Удалить мои данные',
  ConsentWithdrawal: 'Отозвать согласие',
  Complaint: 'Жалоба',
}

/**
 * `/data-request` — API_CONTRACT_CYCLE5.md §48.1, §48.4. Public, no account needed: the person who
 * booked as a guest has no other way to reach us. Deliberately does not say anything about whether
 * the phone number is known to the system — the response is identical either way (§48.1), so the
 * frontend must not try to read anything into it either.
 */
export function SubjectRequestPage() {
  const [kind, setKind] = useState<SubjectRequestKind>('Access')
  const [phone, setPhone] = useState('')
  const [contactValue, setContactValue] = useState('')
  const [message, setMessage] = useState('')
  const [captchaToken, setCaptchaToken] = useState('')

  const mut = useMutation({
    mutationFn: () => subjectRequestsApi.submit({ kind, phone, contactValue, message, captchaToken: captchaToken || undefined }),
  })

  if (mut.isSuccess) {
    return (
      <div className="max-w-md mx-auto px-6 pt-16 pb-24 text-center">
        <Card className="p-10">
          <div className="w-14 h-14 rounded-full bg-success-bg flex items-center justify-center mx-auto mb-4">
            <Icon name="check" size={26} strokeWidth={1.8} className="text-success" />
          </div>
          <h1 className="font-serif text-xl font-medium text-ink mb-2">Обращение принято</h1>
          <p className="text-sm text-ink-soft mb-1">
            Номер обращения: <strong>{mut.data.reference}</strong>
          </p>
          <p className="text-sm text-ink-soft">
            Мы ответим в течение {mut.data.responseDueByWorkingDays} рабочих дней на указанный контакт.
          </p>
        </Card>
      </div>
    )
  }

  return (
    <div className="max-w-md mx-auto px-6 pt-16 pb-24">
      <h1 className="font-serif text-2xl font-medium text-ink mb-2 text-center">Обращение по своим данным</h1>
      <p className="text-sm text-ink-soft mb-7 text-center">
        Для тех, кто записывался без учётной записи, или хочет обратиться письменно. Если у вас есть аккаунт, часть
        этого доступна сразу в{' '}
        <Link to="/profile/consents" className="text-gold hover:text-gold-dark">
          профиле
        </Link>
        .
      </p>

      <Card className="p-[26px]">
        <form
          onSubmit={(e) => {
            e.preventDefault()
            mut.mutate()
          }}
          className="flex flex-col gap-4"
        >
          <div className="flex flex-col gap-1.5">
            <label htmlFor="subject-request-kind" className="text-[13px] font-medium text-[#4A4038]">
              Тип обращения
            </label>
            <select
              id="subject-request-kind"
              value={kind}
              onChange={(e) => setKind(e.target.value as SubjectRequestKind)}
              className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold bg-white text-ink"
            >
              {Object.entries(KIND_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>

          <Input
            label="Номер телефона, с которым вы записывались"
            type="tel"
            placeholder="+7 999 000 00 00"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
          />
          <Input
            label="Контакт для ответа (телефон или e-mail)"
            value={contactValue}
            onChange={(e) => setContactValue(e.target.value)}
          />

          <div className="flex flex-col gap-1.5">
            <label htmlFor="subject-request-message" className="text-[13px] font-medium text-[#4A4038]">
              Опишите обращение
            </label>
            <textarea
              id="subject-request-message"
              rows={4}
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink"
            />
          </div>

          {smartCaptchaEnabled && (
            <div className="flex flex-col gap-1">
              <SmartCaptcha onToken={setCaptchaToken} />
              <p className="text-xs text-muted">Подтвердите, что вы не робот (Yandex SmartCaptcha)</p>
            </div>
          )}

          {mut.isError && <p className="text-sm text-danger">{getSubjectRequestErrorMessage(mut.error)}</p>}

          <Button
            type="submit"
            size="lg"
            loading={mut.isPending}
            disabled={!phone || !contactValue || !message || (smartCaptchaEnabled && !captchaToken)}
            className="w-full"
          >
            Отправить обращение
          </Button>
        </form>
      </Card>
    </div>
  )
}
