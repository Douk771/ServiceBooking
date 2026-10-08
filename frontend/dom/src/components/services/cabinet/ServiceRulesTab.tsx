import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { InlineError } from '@/components/ui/InlineError'
import { staysServicesApi } from '../../../api/staysServices'
import type { ServiceManageDto, StaysServiceConflictDto } from '../../../types'
import {
  NO_PREPAY_WARNING,
  PUBLISH_PROBLEM_TEXT,
  isSetupDirty,
  setupDraftOf,
  setupFieldOfError,
  toSetupInput,
  validateSetup,
  type SetupDraft,
  type SetupErrors,
} from '../../../utils/serviceForms'
import { getStayErrorMessage, httpStatus, plainBody, readConflict } from '../../../utils/stayError'
import { NumberField, SavedNote, SectionCard, SelectField, SwitchRow } from '../../cabinet/formParts'
import { StayNotice } from '../../StayNotice'
import { ConfirmDialog } from '../ConfirmDialog'
import { useServiceTab } from './serviceContext'

const HOURS = Array.from({ length: 12 }, (_, i) => ({ value: String(i + 1), label: String(i + 1) }))
const LEAD_OPTIONS = Array.from({ length: 97 }, (_, i) => ({ value: String(i * 30), label: i * 30 === 0 ? 'без ограничения' : i * 30 < 60 ? `${i * 30} минут` : `${(i * 30) / 60} ч` }))

/** «Правила»: hours, step, preparation gap, prepayment, cancellation, publication (`ManageServices`). */
export function ServiceRulesTab() {
  const { canManage, service } = useServiceTab()
  if (!canManage) return <p className="text-sm text-ink-soft">Правила услуги задаёт владелец.</p>
  // keyed by the saved DTO so the draft restarts after a save
  return <RulesForm key={JSON.stringify(setupDraftOf(service))} />
}

function RulesForm() {
  const { companyId, service, setService } = useServiceTab()
  const [d, setD] = useState<SetupDraft>(() => setupDraftOf(service))
  const [errors, setErrors] = useState<SetupErrors>({})
  const [formError, setFormError] = useState('')
  const [saved, setSaved] = useState(false)
  const set = <K extends keyof SetupDraft>(key: K, value: SetupDraft[K]) => {
    setD((v) => ({ ...v, [key]: value }))
    setSaved(false)
  }
  const dirty = isSetupDirty(d, setupDraftOf(service))

  const save = useMutation({
    mutationFn: () => staysServicesApi.setup(companyId, service.id, toSetupInput(d)),
    onSuccess: (s) => {
      setService(s)
      setSaved(true)
      setErrors({})
      setFormError('')
    },
    onError: (err) => {
      const text = plainBody(err)
      const field = httpStatus(err) === 400 ? setupFieldOfError(text) : null
      if (field) setErrors({ [field]: text })
      else setFormError(getStayErrorMessage(err, 'Не удалось сохранить правила.'))
    },
  })

  const submit = () => {
    setFormError('')
    const e = validateSetup(d, service.cancellationBoundaryRange)
    setErrors(e)
    if (Object.keys(e).length === 0) save.mutate()
  }

  return (
    <div className="flex flex-col gap-6">
      <form
        noValidate
        className="flex flex-col gap-5"
        onSubmit={(e) => {
          e.preventDefault()
          submit()
        }}
      >
        <SectionCard title="Название и адрес">
          <Input label="Название" maxLength={100} value={d.name} error={errors.name} onChange={(e) => set('name', e.target.value)} />
          <div>
            <Input label="Адрес услуги" value={d.slug} error={errors.slug} onChange={(e) => set('slug', e.target.value.toLowerCase())} />
            <p className="mt-1 break-all text-xs text-muted">Страница для гостей: {service.publicUrl}</p>
          </div>
        </SectionCard>

        <SectionCard title="Время сеанса">
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectField label="Минимум часов" value={String(d.minHours)} options={HOURS} onChange={(v) => set('minHours', Number(v))} error={errors.minHours} />
            <SelectField label="Максимум часов" value={String(d.maxHours)} options={HOURS} onChange={(v) => set('maxHours', Number(v))} error={errors.maxHours} />
            <SelectField
              label="Шаг старта"
              value={String(d.stepMinutes)}
              options={[
                { value: '60', label: 'Каждый час' },
                { value: '30', label: 'Каждые 30 минут' },
              ]}
              onChange={(v) => set('stepMinutes', v === '30' ? 30 : 60)}
              error={errors.stepMinutes}
            />
            <SelectField
              label="Нельзя бронировать позже, чем за"
              value={String(d.minLeadMinutes)}
              options={LEAD_OPTIONS}
              onChange={(v) => set('minLeadMinutes', Number(v))}
              error={errors.minLeadMinutes}
              hint="Минимальное время до начала сеанса"
            />
            <NumberField label="Время на подготовку после сеанса" suffix="минут" hint="От 0 до 240, шаг 15. Это время занято и недоступно для записи" value={d.bufferMinutes} onChange={(v) => set('bufferMinutes', v)} error={errors.bufferMinutes} min={0} max={240} />
          </div>
          <SwitchRow label="Показывать время на подготовку гостям" hint="На странице услуги появится строка «Время на подготовку»" checked={d.showBufferToGuests} onChange={(v) => set('showBufferToGuests', v)} />
          <SwitchRow label="Можно добавлять к броням домов" hint="Гость добавит услугу к проживанию; оплата на месте" checked={d.availableForHouseBookings} onChange={(v) => set('availableForHouseBookings', v)} />
        </SectionCard>

        <SectionCard title="Предоплата" description="Действует для заказа услуги без проживания. К броне дома услуга всегда оплачивается на месте.">
          <label className="flex min-h-[44px] cursor-pointer items-center gap-3 text-sm text-ink">
            <input type="checkbox" checked={d.prepayPercent == null} onChange={(e) => set('prepayPercent', e.target.checked ? null : 30)} className="h-5 w-5 accent-gold" />
            Без предоплаты — оплата на месте
          </label>
          {d.prepayPercent != null && (
            <NumberField label="Размер предоплаты" suffix="%" value={d.prepayPercent} onChange={(v) => set('prepayPercent', v)} error={errors.prepayPercent} min={1} max={100} />
          )}
          {d.prepayPercent == null && (
            <p role="alert" className="rounded-2xl bg-warning-bg px-4 py-3 text-sm text-warning" data-testid="no-prepay-warning">
              {NO_PREPAY_WARNING}
            </p>
          )}
        </SectionCard>

        <SectionCard title="Отмена гостем" description="Применяется при предоплате. Если сеанс не оплачен, гость отменяет без последствий.">
          <fieldset className="flex flex-col gap-2.5">
            <legend className="sr-only">Шаблон отмены сеанса</legend>
            {[
              { value: 'NoDeductions' as const, title: 'Без удержаний', text: 'Отмена до начала — вся предоплата возвращается.' },
              {
                value: 'PreparationCosts' as const,
                title: 'Расходы на подготовку',
                text: 'Отмена до срока — вся предоплата. Позже компания вправе удержать только фактические расходы на подготовку, не больше стоимости первого часа.',
              },
            ].map((t) => (
              <label key={t.value} className={`flex min-h-[44px] cursor-pointer items-start gap-3 rounded-2xl border p-4 ${d.cancellationPolicy === t.value ? 'border-ink bg-cream-deep/50' : 'border-line hover:border-line-strong'}`}>
                <input type="radio" name="service-cancellation" className="mt-1 h-5 w-5 accent-gold" checked={d.cancellationPolicy === t.value} onChange={() => set('cancellationPolicy', t.value)} />
                <span>
                  <span className="block text-sm font-semibold text-ink">{t.title}</span>
                  <span className="mt-0.5 block text-xs leading-relaxed text-ink-soft">{t.text}</span>
                </span>
              </label>
            ))}
          </fieldset>
          {d.cancellationPolicy === 'PreparationCosts' && (
            <NumberField
              label="Срок для полного возврата"
              suffix="часов до начала"
              hint={`От ${service.cancellationBoundaryRange.min} до ${service.cancellationBoundaryRange.max}. Ставьте близко к моменту, когда вы начинаете подготовку`}
              value={d.cancellationBoundaryHours}
              onChange={(v) => set('cancellationBoundaryHours', v)}
              error={errors.cancellationBoundaryHours}
              min={service.cancellationBoundaryRange.min}
              max={service.cancellationBoundaryRange.max}
            />
          )}
          <StayNotice textKey="StayServiceCancellationOwnerNotice" />
        </SectionCard>

        {formError && <InlineError>{formError}</InlineError>}
        <div className="flex items-center gap-3">
          <Button type="submit" size="lg" loading={save.isPending} disabled={!dirty} className="min-h-[44px]">
            Сохранить правила
          </Button>
          <SavedNote show={saved} />
        </div>
      </form>

      <PublicationCard dirty={dirty} />
    </div>
  )
}

function PublicationCard({ dirty }: { dirty: boolean }) {
  const { companyId, service, setService } = useServiceTab()
  const navigate = useNavigate()
  const [error, setError] = useState('')
  const [confirm, setConfirm] = useState<'archive' | 'delete' | null>(null)
  const done = (s: ServiceManageDto) => {
    setService(s)
    setError('')
    setConfirm(null)
  }
  const fail = (err: unknown) => {
    setConfirm(null)
    const c = readConflict<StaysServiceConflictDto>(err)
    setError(c?.message ?? getStayErrorMessage(err, 'Не удалось выполнить действие.'))
  }
  const publish = useMutation({ mutationFn: () => staysServicesApi.publish(companyId, service.id), onSuccess: done, onError: fail })
  const unpublish = useMutation({ mutationFn: () => staysServicesApi.unpublish(companyId, service.id), onSuccess: done, onError: fail })
  const archive = useMutation({ mutationFn: () => staysServicesApi.archive(companyId, service.id), onSuccess: done, onError: fail })
  const remove = useMutation({
    mutationFn: () => staysServicesApi.remove(companyId, service.id),
    onSuccess: () => navigate(`/cabinet/${companyId}/services`, { replace: true }),
    onError: fail,
  })

  const problems = service.publishProblems.filter((p) => p !== 'ServiceArchived' || service.isArchived)
  const status = service.isArchived ? 'В архиве' : service.isPublished ? 'Опубликована' : 'Не опубликована'

  return (
    <SectionCard title="Публикация" description={`Сейчас: ${status}.`}>
      {!service.isPublished && !service.isArchived && problems.length > 0 && (
        <ul className="list-disc rounded-2xl bg-warning-bg py-3 pl-8 pr-4 text-sm text-warning" data-testid="publish-problems">
          {problems.map((p) => (
            <li key={p}>{PUBLISH_PROBLEM_TEXT[p] ?? p}</li>
          ))}
        </ul>
      )}
      {dirty && <p className="text-xs text-muted">Есть несохранённые правила — сначала сохраните их.</p>}
      {error && <InlineError>{error}</InlineError>}
      <div className="flex flex-wrap gap-3">
        {!service.isArchived && !service.isPublished && (
          <Button className="min-h-[44px]" loading={publish.isPending} disabled={problems.length > 0 || dirty} onClick={() => publish.mutate()}>
            Опубликовать
          </Button>
        )}
        {service.isPublished && (
          <Button variant="secondary" className="min-h-[44px]" loading={unpublish.isPending} onClick={() => unpublish.mutate()}>
            Снять с публикации
          </Button>
        )}
        {!service.isArchived && (
          <Button variant="secondary" className="min-h-[44px]" onClick={() => setConfirm('archive')}>
            В архив
          </Button>
        )}
        <Button variant="danger" className="min-h-[44px]" onClick={() => setConfirm('delete')}>
          Удалить услугу
        </Button>
      </div>
      {service.hasSessions && <p className="text-xs text-muted">У услуги есть сеансы — удалить её нельзя, только отправить в архив.</p>}
      {confirm === 'archive' && (
        <ConfirmDialog title="Отправить услугу в архив?" text="Гости больше не увидят услугу и не смогут её заказать. Уже оформленные сеансы остаются в силе." confirmLabel="В архив" pending={archive.isPending} onConfirm={() => archive.mutate()} onClose={() => setConfirm(null)} />
      )}
      {confirm === 'delete' && (
        <ConfirmDialog title="Удалить услугу?" text="Услуга, её цены и позиции будут удалены. Если у услуги были сеансы, сервер не даст её удалить — тогда используйте архив." confirmLabel="Удалить" pending={remove.isPending} onConfirm={() => remove.mutate()} onClose={() => setConfirm(null)} />
      )}
    </SectionCard>
  )
}
