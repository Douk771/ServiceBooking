import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { plansApi, type PlanConfig } from '../../api/plans'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'

interface PlanForm {
  name: string
  pricePerMonth: string
  maxEmployees: string
  maxCompanies: string
  allowOnlineBooking: boolean
  allowMailing: boolean
  allowAnalytics: boolean
  allowPublicListing: boolean
  allowOnlinePayment: boolean
  description: string
  notifyDaysBefore: string
}

const defaultForm: PlanForm = {
  name: '',
  pricePerMonth: '0',
  maxEmployees: '',
  maxCompanies: '',
  allowOnlineBooking: true,
  allowMailing: false,
  allowAnalytics: false,
  allowPublicListing: true,
  allowOnlinePayment: false,
  description: '',
  notifyDaysBefore: '7',
}

function featureIcon(enabled: boolean) {
  return enabled ? '✓' : '✗'
}

function FeatureBadge({ label, enabled }: { label: string; enabled: boolean }) {
  return (
    <span className={`inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full font-medium ${
      enabled ? 'bg-green-50 text-green-700' : 'bg-gray-100 text-gray-400'
    }`}>
      {featureIcon(enabled)} {label}
    </span>
  )
}

export function PlansTab() {
  const qc = useQueryClient()
  const [showCreate, setShowCreate] = useState(false)
  const [form, setForm] = useState<PlanForm>(defaultForm)
  const [editingPlan, setEditingPlan] = useState<PlanConfig | null>(null)

  const { data: plans, isLoading } = useQuery({
    queryKey: ['admin-plans'],
    queryFn: plansApi.list,
  })

  const formToPayload = () => ({
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
    isActive: true,
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

  const openCreate = () => {
    setForm(defaultForm)
    setEditingPlan(null)
    setShowCreate(true)
  }

  const openEdit = (plan: PlanConfig) => {
    setForm({
      name: plan.name,
      pricePerMonth: String(plan.pricePerMonth),
      maxEmployees: plan.maxEmployees !== null ? String(plan.maxEmployees) : '',
      maxCompanies: plan.maxCompanies !== null ? String(plan.maxCompanies) : '',
      allowOnlineBooking: plan.allowOnlineBooking,
      allowMailing: plan.allowMailing,
      allowAnalytics: plan.allowAnalytics,
      allowPublicListing: plan.allowPublicListing,
      allowOnlinePayment: plan.allowOnlinePayment,
      description: plan.description ?? '',
      notifyDaysBefore: String(plan.notifyDaysBefore),
    })
    setEditingPlan(plan)
    setShowCreate(true)
  }

  const active = plans?.filter(p => p.isActive) ?? []
  const inactive = plans?.filter(p => !p.isActive) ?? []

  return (
    <div>
      <div className="flex items-center justify-between mb-5">
        <p className="text-sm text-gray-500">Тарифные планы подписки</p>
        <Button onClick={openCreate}>+ Создать тариф</Button>
      </div>

      {isLoading ? (
        <div className="grid gap-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-24 bg-gray-100 rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : (
        <>
          {active.length > 0 && (
            <div className="grid gap-4 mb-6">
              {active.map(plan => (
                <Card key={plan.id} className="p-5">
                  <div className="flex items-start justify-between gap-4 flex-wrap">
                    <div className="flex-1">
                      <div className="flex items-center gap-3 mb-2 flex-wrap">
                        <h3 className="font-semibold text-gray-900 text-lg">{plan.name}</h3>
                        <span className="text-sm font-medium text-primary-700">
                          {plan.pricePerMonth > 0 ? `${plan.pricePerMonth.toLocaleString('ru-RU')} ₽/мес` : 'Бесплатно'}
                        </span>
                        <span className="text-xs text-gray-400">
                          Сотрудников: {plan.maxEmployees !== null ? `до ${plan.maxEmployees}` : '∞'}
                        </span>
                        <span className="text-xs text-gray-400">
                          Компаний: {plan.maxCompanies !== null ? `до ${plan.maxCompanies}` : '∞'}
                        </span>
                      </div>
                      <div className="flex flex-wrap gap-1.5 mb-2">
                        <FeatureBadge label="Онлайн-запись" enabled={plan.allowOnlineBooking} />
                        <FeatureBadge label="Рассылка" enabled={plan.allowMailing} />
                        <FeatureBadge label="Аналитика" enabled={plan.allowAnalytics} />
                        <FeatureBadge label="В общем списке" enabled={plan.allowPublicListing} />
                        <FeatureBadge label="Онлайн-оплата" enabled={plan.allowOnlinePayment} />
                      </div>
                      {plan.description && (
                        <p className="text-sm text-gray-500">{plan.description}</p>
                      )}
                      <p className="text-xs text-gray-400 mt-1">
                        Уведомление за {plan.notifyDaysBefore} дн. до деактивации
                      </p>
                    </div>
                    <div className="flex gap-2 shrink-0">
                      <Button variant="secondary" size="sm" onClick={() => openEdit(plan)}>
                        Редактировать
                      </Button>
                      <Button
                        variant="danger"
                        size="sm"
                        loading={deactivateMut.isPending}
                        onClick={() => deactivateMut.mutate(plan.id)}
                      >
                        Деактивировать
                      </Button>
                    </div>
                  </div>
                </Card>
              ))}
            </div>
          )}

          {inactive.length > 0 && (
            <div>
              <p className="text-xs font-medium text-gray-400 uppercase tracking-wide mb-2">Неактивные</p>
              <div className="grid gap-2">
                {inactive.map(plan => (
                  <div key={plan.id} className="flex items-center gap-3 px-4 py-3 rounded-xl bg-gray-50 opacity-60">
                    <span className="font-medium text-gray-500 line-through">{plan.name}</span>
                    <span className="text-xs text-gray-400">
                      {plan.pricePerMonth > 0 ? `${plan.pricePerMonth} ₽/мес` : 'Бесплатно'}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {plans?.length === 0 && (
            <Card className="p-12 text-center text-gray-400">
              <p className="text-3xl mb-2">📋</p>
              <p>Тарифов ещё нет</p>
            </Card>
          )}
        </>
      )}

      {showCreate && (
        <Modal title={editingPlan ? 'Редактировать тариф' : 'Создать тариф'} onClose={() => setShowCreate(false)}>
          <div className="flex flex-col gap-4">
            <Input
              label="Название *"
              placeholder="Basic"
              value={form.name}
              onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
            />
            <div className="grid grid-cols-3 gap-3">
              <Input
                label="Цена/мес (₽)"
                type="number"
                min={0}
                value={form.pricePerMonth}
                onChange={e => setForm(f => ({ ...f, pricePerMonth: e.target.value }))}
              />
              <Input
                label="Макс. сотрудников (∞)"
                type="number"
                min={1}
                value={form.maxEmployees}
                onChange={e => setForm(f => ({ ...f, maxEmployees: e.target.value }))}
                placeholder="∞"
              />
              <Input
                label="Макс. компаний (∞)"
                type="number"
                min={1}
                value={form.maxCompanies}
                onChange={e => setForm(f => ({ ...f, maxCompanies: e.target.value }))}
                placeholder="∞"
              />
            </div>

            <div>
              <p className="text-sm font-medium text-gray-700 mb-2">Функции</p>
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
                      onChange={e => setForm(f => ({ ...f, [key]: e.target.checked }))}
                      className="w-4 h-4 accent-orange-500"
                    />
                    <span className="text-sm text-gray-700">{label}</span>
                  </label>
                ))}
              </div>
            </div>

            <div className="flex flex-col gap-1">
              <label className="text-sm font-medium text-gray-700">Описание</label>
              <textarea
                rows={2}
                value={form.description}
                onChange={e => setForm(f => ({ ...f, description: e.target.value }))}
                placeholder="Для малого бизнеса"
                className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100 resize-none"
              />
            </div>

            <Input
              label="Дней уведомления до деактивации"
              type="number"
              min={0}
              value={form.notifyDaysBefore}
              onChange={e => setForm(f => ({ ...f, notifyDaysBefore: e.target.value }))}
            />

            {(createMut.isError || updateMut.isError) && (
              <p className="text-sm text-red-500">{editingPlan ? 'Ошибка при сохранении тарифа' : 'Ошибка при создании тарифа'}</p>
            )}

            <div className="flex gap-3 pt-1">
              <Button type="button" variant="secondary" className="flex-1" onClick={() => setShowCreate(false)}>
                Отмена
              </Button>
              <Button
                className="flex-1"
                loading={editingPlan ? updateMut.isPending : createMut.isPending}
                onClick={() => editingPlan ? updateMut.mutate() : createMut.mutate()}
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
