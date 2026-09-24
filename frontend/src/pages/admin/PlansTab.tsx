import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  plansApi,
  type PlanConfig,
  type PhotoRetention,
  type OptionAvailability,
  type AdminPlanInput,
  type AdminOptionDto,
} from '../../api/plans'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { Icon } from '../../components/ui/Icon'
import { getPlanErrorMessage } from '../../utils/planError'
import {
  MAX_HIGHLIGHTS,
  PUBLIC_MAX_HIGHLIGHTS,
  MAX_HIGHLIGHT_LENGTH,
  defaultForm,
  optionRulesToForm,
  optionRulesToPayload,
  type PlanForm,
} from './planForm'

const RETENTION_LABELS: Record<PhotoRetention, string> = {
  SixMonths: '6 месяцев',
  TwelveMonths: '12 месяцев',
}

// A server on an older release (pre cycle-7 AdminPlanDto) never sent highlights/options/isSystemFree/
// photoRetention at all — `plan.photoRetention` can then be `undefined` and miss this map entirely.
// Falling back to a plain label instead of indexing straight into RETENTION_LABELS keeps the card
// readable instead of throwing (§100.2 / US-110).
const UNKNOWN_RETENTION_LABEL = 'срок не указан'

function retentionLabel(retention: PhotoRetention | undefined | null): string {
  if (!retention) return UNKNOWN_RETENTION_LABEL
  return RETENTION_LABELS[retention] ?? UNKNOWN_RETENTION_LABEL
}

const AVAILABILITY_LABELS: Record<OptionAvailability, string> = {
  Unavailable: 'Недоступна',
  Included: 'Включена',
  Extra: 'За доплату',
}

function featureIcon(enabled: boolean) {
  return enabled ? '✓' : '✗'
}

function FeatureBadge({ label, enabled }: { label: string; enabled: boolean }) {
  return (
    <span
      className={`inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full font-medium ${
        enabled ? 'bg-success-bg text-success' : 'bg-cream-deep text-muted'
      }`}
    >
      {featureIcon(enabled)} {label}
    </span>
  )
}


export function PlansTab() {
  const qc = useQueryClient()
  const [showCreate, setShowCreate] = useState(false)
  const [form, setForm] = useState<PlanForm>(defaultForm)
  const [editingPlan, setEditingPlan] = useState<PlanConfig | null>(null)
  const [highlightsError, setHighlightsError] = useState('')

  const { data: plans, isLoading } = useQuery({
    queryKey: ['admin-plans'],
    queryFn: plansApi.list,
  })

  const {
    data: catalogOptions,
    isLoading: optionsLoading,
    isError: optionsError,
  } = useQuery({
    queryKey: ['admin-options'],
    queryFn: plansApi.listOptions,
  })

  const formToPayload = (): AdminPlanInput => ({
    name: form.name,
    pricePerMonth: parseFloat(form.pricePerMonth) || 0,
    maxEmployees: form.maxEmployees ? parseInt(form.maxEmployees) : null,
    maxCompanies: form.maxCompanies ? parseInt(form.maxCompanies) : null,
    allowOnlineBooking: form.allowOnlineBooking,
    allowMailing: form.allowMailing,
    allowAnalytics: form.allowAnalytics,
    allowPublicListing: form.allowPublicListing,
    allowOnlinePayment: form.allowOnlinePayment,
    description: form.description || null,
    notifyDaysBefore: parseInt(form.notifyDaysBefore) || 7,
    photoQuotaMb: form.photoQuotaMb.trim() === '' ? null : parseInt(form.photoQuotaMb),
    photoRetention: form.photoRetention,
    isActive: true,
    isPublic: true,
    sortOrder: 0,
    highlights: form.highlights,
    options: optionRulesToPayload(form.optionRules),
  })

  const createMut = useMutation({
    mutationFn: () => plansApi.create(formToPayload()),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-plans'] })
      setShowCreate(false)
      setForm(defaultForm)
    },
  })

  const updateMut = useMutation({
    mutationFn: () => plansApi.update(editingPlan!.id, formToPayload()),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-plans'] })
      setShowCreate(false)
      setEditingPlan(null)
      setForm(defaultForm)
    },
  })

  const deactivateMut = useMutation({
    mutationFn: (id: string) => plansApi.deactivate(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-plans'] }),
  })

  // US-05: reactivates a plan that was previously deactivated — same PUT the edit form uses, just with
  // the plan's own current values and isActive forced true, so it doesn't require opening the form.
  const activateMut = useMutation({
    mutationFn: (plan: PlanConfig) =>
      plansApi.update(plan.id, {
        name: plan.name,
        pricePerMonth: plan.pricePerMonth,
        maxEmployees: plan.maxEmployees,
        maxCompanies: plan.maxCompanies,
        allowOnlineBooking: plan.allowOnlineBooking,
        allowMailing: plan.allowMailing,
        allowAnalytics: plan.allowAnalytics,
        allowPublicListing: plan.allowPublicListing,
        allowOnlinePayment: plan.allowOnlinePayment,
        description: plan.description,
        notifyDaysBefore: plan.notifyDaysBefore,
        photoQuotaMb: plan.photoQuotaMb,
        photoRetention: plan.photoRetention ?? 'TwelveMonths',
        isActive: true,
        isPublic: plan.isPublic,
        sortOrder: plan.sortOrder,
        highlights: plan.highlights ?? [],
        options: plan.options ?? [],
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-plans'] }),
  })

  // Отдельный маршрут PUT /admin/plans/{id}/system-free — контракт нарочно убрал isSystemFree из
  // AdminPlanInput (см. плансApi.setSystemFree), чтобы обычное сохранение полей не могло случайно
  // переставить системный бесплатный тариф.
  const systemFreeMut = useMutation({
    mutationFn: ({ id, isSystemFree }: { id: string; isSystemFree: boolean }) =>
      plansApi.setSystemFree(id, isSystemFree),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-plans'] }),
  })

  // react-query keeps a mutation's error until the next mutate, so without an explicit reset the
  // modal reopens showing the previous attempt's failure — potentially from the other mutation, and
  // about a different plan.
  const closeModal = () => {
    setShowCreate(false)
    createMut.reset()
    updateMut.reset()
    setHighlightsError('')
  }

  const openCreate = () => {
    setForm(defaultForm)
    setEditingPlan(null)
    createMut.reset()
    updateMut.reset()
    setHighlightsError('')
    setShowCreate(true)
  }

  const openEdit = (plan: PlanConfig) => {
    createMut.reset()
    updateMut.reset()
    setHighlightsError('')
    setForm({
      name: plan.name,
      pricePerMonth: String(plan.pricePerMonth),
      maxEmployees: plan.maxEmployees != null ? String(plan.maxEmployees) : '',
      maxCompanies: plan.maxCompanies != null ? String(plan.maxCompanies) : '',
      allowOnlineBooking: plan.allowOnlineBooking,
      allowMailing: plan.allowMailing,
      allowAnalytics: plan.allowAnalytics,
      allowPublicListing: plan.allowPublicListing,
      allowOnlinePayment: plan.allowOnlinePayment,
      description: plan.description ?? '',
      notifyDaysBefore: String(plan.notifyDaysBefore),
      photoQuotaMb: plan.photoQuotaMb != null ? String(plan.photoQuotaMb) : '',
      photoRetention: plan.photoRetention ?? 'TwelveMonths',
      highlights: plan.highlights ?? [],
      optionRules: optionRulesToForm(plan.options ?? []),
    })
    setEditingPlan(plan)
    setShowCreate(true)
  }

  const addHighlight = () => {
    if (form.highlights.length >= MAX_HIGHLIGHTS) {
      setHighlightsError(`Больше ${MAX_HIGHLIGHTS} пунктов сохранить нельзя.`)
      return
    }
    setHighlightsError('')
    setForm((f) => ({ ...f, highlights: [...f.highlights, ''] }))
  }

  const updateHighlight = (index: number, value: string) => {
    if (value.length > MAX_HIGHLIGHT_LENGTH) {
      setHighlightsError(`Пункт не может быть длиннее ${MAX_HIGHLIGHT_LENGTH} символов.`)
      return
    }
    setHighlightsError('')
    setForm((f) => ({ ...f, highlights: f.highlights.map((h, i) => (i === index ? value : h)) }))
  }

  const removeHighlight = (index: number) => {
    setHighlightsError('')
    setForm((f) => ({ ...f, highlights: f.highlights.filter((_, i) => i !== index) }))
  }

  const setOptionAvailability = (optionId: string, availability: OptionAvailability) => {
    setForm((f) => ({
      ...f,
      optionRules: {
        ...f.optionRules,
        [optionId]: {
          availability,
          includedQuantity: f.optionRules[optionId]?.includedQuantity ?? '',
        },
      },
    }))
  }

  const setOptionIncludedQuantity = (optionId: string, value: string) => {
    setForm((f) => ({
      ...f,
      optionRules: {
        ...f.optionRules,
        [optionId]: {
          availability: f.optionRules[optionId]?.availability ?? 'Included',
          includedQuantity: value,
        },
      },
    }))
  }

  const active = plans?.filter((p) => p.isActive) ?? []
  const inactive = plans?.filter((p) => !p.isActive) ?? []

  const renderOptionRow = (option: AdminOptionDto) => {
    const rule = form.optionRules[option.id] ?? {
      availability: 'Unavailable' as OptionAvailability,
      includedQuantity: '',
    }
    return (
      <div key={option.id} className="flex items-center justify-between gap-3 py-2 border-b border-line last:border-0">
        <div className="min-w-0">
          <p className="text-sm font-medium text-ink truncate">{option.name}</p>
          <p className="text-xs text-muted">
            {option.kind === 'Quantity' ? (option.unitName ?? 'за единицу') : 'вкл/выкл'}
          </p>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <select
            value={rule.availability}
            onChange={(e) => setOptionAvailability(option.id, e.target.value as OptionAvailability)}
            className="rounded-lg border border-line px-2.5 py-1.5 text-xs outline-none focus:border-gold bg-white text-ink"
          >
            {(Object.keys(AVAILABILITY_LABELS) as OptionAvailability[]).map((a) => (
              <option key={a} value={a}>
                {AVAILABILITY_LABELS[a]}
              </option>
            ))}
          </select>
          {option.kind === 'Quantity' && rule.availability === 'Included' && (
            <input
              type="number"
              min={1}
              placeholder="кол-во"
              value={rule.includedQuantity}
              onChange={(e) => setOptionIncludedQuantity(option.id, e.target.value)}
              className="w-20 rounded-lg border border-line px-2 py-1.5 text-xs outline-none focus:border-gold"
            />
          )}
        </div>
      </div>
    )
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-5">
        <p className="text-sm text-muted">Тарифные планы подписки</p>
        <Button onClick={openCreate}>
          <Icon name="plus" size={15} strokeWidth={2} /> Создать тариф
        </Button>
      </div>

      {isLoading ? (
        <div className="grid gap-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-24 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : (
        <>
          {active.length > 0 && (
            <div className="grid gap-4 mb-6">
              {active.map((plan) => (
                <Card key={plan.id} className="p-5">
                  <div className="flex items-start justify-between gap-4 flex-wrap">
                    <div className="flex-1">
                      <div className="flex items-center gap-3 mb-2 flex-wrap">
                        <h3 className="font-semibold text-ink text-lg">{plan.name}</h3>
                        {plan.isSystemFree && (
                          <span className="text-xs font-semibold px-2 py-0.5 rounded-full bg-cream-deep text-gold-dark">
                            Системный бесплатный
                          </span>
                        )}
                        <span className="text-sm font-medium text-gold-dark">
                          {plan.pricePerMonth > 0 ? `${plan.pricePerMonth.toLocaleString('ru-RU')} ₽/мес` : 'Бесплатно'}
                        </span>
                        <span className="text-xs text-muted">
                          Сотрудников суммарно: {plan.maxEmployees != null ? `до ${plan.maxEmployees}` : '∞'}
                        </span>
                        <span className="text-xs text-muted">
                          Компаний суммарно: {plan.maxCompanies != null ? `до ${plan.maxCompanies}` : '∞'}
                        </span>
                      </div>
                      <div className="flex flex-wrap gap-1.5 mb-2">
                        <FeatureBadge label="Онлайн-запись" enabled={plan.allowOnlineBooking} />
                        <FeatureBadge label="Рассылка" enabled={plan.allowMailing} />
                        <FeatureBadge label="Аналитика" enabled={plan.allowAnalytics} />
                        <FeatureBadge label="В общем списке" enabled={plan.allowPublicListing} />
                        <FeatureBadge label="Онлайн-оплата" enabled={plan.allowOnlinePayment} />
                      </div>
                      {(plan.highlights ?? []).length > 0 && (
                        <ul className="text-xs text-ink-soft list-disc list-inside mb-1">
                          {(plan.highlights ?? []).map((h, i) => (
                            <li key={i}>{h}</li>
                          ))}
                        </ul>
                      )}
                      {plan.description && <p className="text-sm text-muted">{plan.description}</p>}
                      <p className="text-xs text-muted mt-1">
                        Уведомление за {plan.notifyDaysBefore} дн. до деактивации
                      </p>
                      <p className="text-xs text-muted mt-0.5">
                        Фото клиентов: {plan.photoQuotaMb != null ? `до ${plan.photoQuotaMb} МБ` : 'без ограничения'} ·{' '}
                        хранятся {retentionLabel(plan.photoRetention)}
                      </p>
                    </div>
                    <div className="flex flex-col items-end gap-1.5 shrink-0">
                      <div className="flex gap-2">
                        <Button variant="secondary" size="sm" onClick={() => openEdit(plan)}>
                          Редактировать
                        </Button>
                        <Button
                          variant="danger"
                          size="sm"
                          loading={deactivateMut.isPending && deactivateMut.variables === plan.id}
                          onClick={() => deactivateMut.mutate(plan.id)}
                        >
                          Деактивировать
                        </Button>
                      </div>
                      {!plan.isSystemFree && (
                        <Button
                          variant="ghost"
                          size="sm"
                          loading={systemFreeMut.isPending && systemFreeMut.variables?.id === plan.id}
                          onClick={() => systemFreeMut.mutate({ id: plan.id, isSystemFree: true })}
                        >
                          Сделать системным бесплатным
                        </Button>
                      )}
                      {deactivateMut.isError && deactivateMut.variables === plan.id && (
                        <p className="text-xs text-danger text-right max-w-[220px]">
                          {getPlanErrorMessage(deactivateMut.error)}
                        </p>
                      )}
                      {systemFreeMut.isError && systemFreeMut.variables?.id === plan.id && (
                        <p className="text-xs text-danger text-right max-w-[220px]">
                          {getPlanErrorMessage(systemFreeMut.error, 'Не удалось изменить системный бесплатный тариф.')}
                        </p>
                      )}
                    </div>
                  </div>
                </Card>
              ))}
            </div>
          )}

          {inactive.length > 0 && (
            <div>
              <p className="text-xs font-medium text-muted uppercase tracking-wide mb-2">Неактивные</p>
              <div className="grid gap-2">
                {inactive.map((plan) => (
                  <div
                    key={plan.id}
                    className="flex items-center justify-between gap-3 px-4 py-3 rounded-xl bg-cream-deep flex-wrap"
                  >
                    <div className="flex items-center gap-3">
                      <span className="font-medium text-muted line-through">{plan.name}</span>
                      <span className="text-xs text-muted">
                        {plan.pricePerMonth > 0 ? `${plan.pricePerMonth} ₽/мес` : 'Бесплатно'}
                      </span>
                    </div>
                    <div className="flex flex-col items-end gap-1">
                      <div className="flex gap-2">
                        <Button variant="secondary" size="sm" onClick={() => openEdit(plan)}>
                          Редактировать
                        </Button>
                        <Button
                          size="sm"
                          loading={activateMut.isPending && activateMut.variables?.id === plan.id}
                          onClick={() => activateMut.mutate(plan)}
                        >
                          Активировать
                        </Button>
                      </div>
                      {activateMut.isError && activateMut.variables?.id === plan.id && (
                        <p className="text-xs text-danger text-right max-w-[220px]">
                          {getPlanErrorMessage(activateMut.error)}
                        </p>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {plans?.length === 0 && (
            <Card className="p-12 text-center text-muted">
              <Icon name="settings" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
              <p>Тарифов ещё нет</p>
            </Card>
          )}
        </>
      )}

      {showCreate && (
        <Modal title={editingPlan ? 'Редактировать тариф' : 'Создать тариф'} onClose={closeModal}>
          <div className="flex flex-col gap-4">
            <Input
              label="Название *"
              placeholder="Basic"
              value={form.name}
              onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
            />
            <div className="grid grid-cols-3 gap-3">
              <Input
                label="Цена/мес (₽)"
                type="number"
                min={0}
                value={form.pricePerMonth}
                onChange={(e) => setForm((f) => ({ ...f, pricePerMonth: e.target.value }))}
              />
              <Input
                label="Макс. сотрудников суммарно (∞)"
                type="number"
                min={1}
                value={form.maxEmployees}
                onChange={(e) => setForm((f) => ({ ...f, maxEmployees: e.target.value }))}
                placeholder="∞"
              />
              <Input
                label="Макс. компаний суммарно (∞)"
                type="number"
                min={1}
                value={form.maxCompanies}
                onChange={(e) => setForm((f) => ({ ...f, maxCompanies: e.target.value }))}
                placeholder="∞"
              />
            </div>

            <div className="grid grid-cols-2 gap-3">
              <Input
                label="Квота фото клиентов, МБ (∞)"
                type="number"
                min={0}
                value={form.photoQuotaMb}
                onChange={(e) => setForm((f) => ({ ...f, photoQuotaMb: e.target.value }))}
                placeholder="∞"
              />
              <div className="flex flex-col gap-1.5">
                <label className="text-[13px] font-medium text-[#4A4038]">Срок хранения фото</label>
                <select
                  value={form.photoRetention}
                  onChange={(e) => setForm((f) => ({ ...f, photoRetention: e.target.value as PhotoRetention }))}
                  className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep bg-white text-ink"
                >
                  <option value="SixMonths">6 месяцев</option>
                  <option value="TwelveMonths">12 месяцев</option>
                </select>
              </div>
            </div>

            <div>
              <p className="text-sm font-medium text-ink-soft mb-2">Функции</p>
              <div className="grid grid-cols-2 gap-2">
                {[
                  { key: 'allowOnlineBooking' as const, label: 'Онлайн-запись' },
                  { key: 'allowMailing' as const, label: 'Рассылка' },
                  { key: 'allowAnalytics' as const, label: 'Аналитика' },
                  { key: 'allowPublicListing' as const, label: 'В общем списке' },
                  { key: 'allowOnlinePayment' as const, label: 'Онлайн-оплата' },
                ].map(({ key, label }) => (
                  <label key={key} className="flex items-center gap-2 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={form[key]}
                      onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.checked }))}
                      className="w-4 h-4 accent-gold"
                    />
                    <span className="text-sm text-ink-soft">{label}</span>
                  </label>
                ))}
              </div>
            </div>

            <div>
              <div className="flex items-center justify-between mb-2">
                <p className="text-sm font-medium text-ink-soft">
                  Пункты тарифа ({form.highlights.length}/{MAX_HIGHLIGHTS})
                </p>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={addHighlight}
                  disabled={form.highlights.length >= MAX_HIGHLIGHTS}
                >
                  <Icon name="plus" size={13} strokeWidth={2} /> Добавить
                </Button>
              </div>
              <p className="text-xs text-muted mb-2">
                На витрине показываются только первые {PUBLIC_MAX_HIGHLIGHTS} — остальные хранятся, но посетители их не
                увидят.
              </p>
              <div className="flex flex-col gap-2">
                {form.highlights.map((h, i) => (
                  <div key={i} className="flex flex-col gap-0.5">
                    <div className="flex items-center gap-2">
                      <input
                        value={h}
                        onChange={(e) => updateHighlight(i, e.target.value)}
                        maxLength={MAX_HIGHLIGHT_LENGTH}
                        placeholder="Онлайн-запись без ограничений"
                        className="flex-1 rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold"
                      />
                      <button
                        type="button"
                        onClick={() => removeHighlight(i)}
                        aria-label="Удалить буллет"
                        className="text-muted hover:text-danger shrink-0"
                      >
                        <Icon name="x" size={14} strokeWidth={2} />
                      </button>
                    </div>
                    {i >= PUBLIC_MAX_HIGHLIGHTS && (
                      <p className="text-[11px] text-muted pl-0.5">
                        Не показывается на витрине (свыше {PUBLIC_MAX_HIGHLIGHTS}-го пункта)
                      </p>
                    )}
                  </div>
                ))}
                {form.highlights.length === 0 && <p className="text-xs text-muted">Буллетов пока нет</p>}
              </div>
              {highlightsError && <p className="text-xs text-danger mt-1.5">{highlightsError}</p>}
            </div>

            <div>
              <p className="text-sm font-medium text-ink-soft mb-2">Доступность опций на тарифе</p>
              {optionsLoading ? (
                <div className="h-24 bg-cream-deep rounded-xl animate-pulse" />
              ) : optionsError ? (
                <p className="text-xs text-danger">
                  Не удалось загрузить каталог опций. Матрица недоступна, остальные поля тарифа можно сохранить как
                  обычно.
                </p>
              ) : catalogOptions && catalogOptions.length > 0 ? (
                <div className="rounded-xl border border-line px-3">{catalogOptions.map(renderOptionRow)}</div>
              ) : (
                <p className="text-xs text-muted">Опций в каталоге ещё нет</p>
              )}
            </div>

            <div className="flex flex-col gap-1">
              <label className="text-sm font-medium text-ink-soft">Описание</label>
              <textarea
                rows={2}
                value={form.description}
                onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
                placeholder="Для малого бизнеса"
                className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold focus:ring-2 focus:ring-cream-deep resize-none"
              />
            </div>

            <Input
              label="Дней уведомления до деактивации"
              type="number"
              min={0}
              value={form.notifyDaysBefore}
              onChange={(e) => setForm((f) => ({ ...f, notifyDaysBefore: e.target.value }))}
            />

            {(editingPlan ? updateMut.isError : createMut.isError) && (
              <p className="text-sm text-danger">
                {getPlanErrorMessage(editingPlan ? updateMut.error : createMut.error, 'Не удалось сохранить тариф.')}
              </p>
            )}

            <div className="flex gap-3 pt-1">
              <Button type="button" variant="secondary" className="flex-1" onClick={closeModal}>
                Отмена
              </Button>
              <Button
                className="flex-1"
                loading={editingPlan ? updateMut.isPending : createMut.isPending}
                onClick={() => (editingPlan ? updateMut.mutate() : createMut.mutate())}
              >
                {editingPlan ? 'Сохранить' : 'Создать'}
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  )
}
