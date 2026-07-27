import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { format } from 'date-fns'
import { profileApi, type ProfilePlanDto } from '../api/profile'
import { useAuthStore } from '../store/authStore'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'

const roleLabel: Record<string, string> = {
  Client: 'Клиент', Master: 'Мастер', CompanyOwner: 'Владелец', SuperAdmin: 'Супер-администратор',
}

function PlanFeature({ label, enabled }: { label: string; enabled: boolean }) {
  return (
    <span className={`inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full font-medium ${
      enabled ? 'bg-green-50 text-green-700' : 'bg-gray-100 text-gray-400'
    }`}>
      {enabled ? '✓' : '✗'} {label}
    </span>
  )
}

function PlanSection({ plan }: { plan: ProfilePlanDto }) {
  const statusLabel = !plan.isActive
    ? { text: 'Отключена', className: 'bg-gray-100 text-gray-500' }
    : plan.isExpired
      ? { text: 'Истекла', className: 'bg-red-50 text-red-600' }
      : { text: 'Активна', className: 'bg-green-50 text-green-700' }

  return (
    <Card className="p-6 mb-5">
      <div className="flex items-center justify-between mb-4 flex-wrap gap-2">
        <h2 className="font-semibold text-gray-900">Тарифный план</h2>
        <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${statusLabel.className}`}>{statusLabel.text}</span>
      </div>
      <div className="flex items-center gap-3 mb-3 flex-wrap">
        <span className="text-lg font-bold text-gray-900">{plan.planName}</span>
        <span className="text-sm font-medium text-primary-700">
          {plan.pricePerMonth > 0 ? `${plan.pricePerMonth.toLocaleString('ru-RU')} ₽/мес` : 'Бесплатно'}
        </span>
        {plan.paidUntil && (
          <span className="text-xs text-gray-400">
            {plan.isExpired ? 'Истёк' : 'Оплачен до'} {format(new Date(plan.paidUntil), 'd MMM yyyy')}
          </span>
        )}
      </div>
      <div className="flex flex-wrap gap-1.5 mb-3">
        <PlanFeature label="Онлайн-запись" enabled={plan.allowOnlineBooking} />
        <PlanFeature label="Рассылка" enabled={plan.allowMailing} />
        <PlanFeature label="Аналитика" enabled={plan.allowAnalytics} />
      </div>
      <div className="flex gap-4 text-xs text-gray-400">
        <span>Сотрудников: {plan.maxEmployees !== null ? `до ${plan.maxEmployees}` : '∞'}</span>
        <span>Компаний: {plan.maxCompanies !== null ? `до ${plan.maxCompanies}` : '∞'}</span>
      </div>
      <p className="text-xs text-gray-400 mt-3">Изменение тарифа и оплата скоро будут доступны здесь же.</p>
    </Card>
  )
}

export function ProfilePage() {
  const { user, setAuth, token } = useAuthStore()
  const qc = useQueryClient()
  const [pwdSuccess, setPwdSuccess] = useState(false)

  const { data: profile, isLoading } = useQuery({
    queryKey: ['profile'],
    queryFn: profileApi.get,
  })

  const isMasterOrOwner = user?.roles.some(r => ['Master', 'CompanyOwner'].includes(r))

  // ── Profile form ──────────────────────────────────────────────────────────
  const { register: regProfile, handleSubmit: hsProfile, formState: { isDirty: pDirty } } = useForm({
    values: profile ? {
      firstName: profile.firstName,
      lastName: profile.lastName,
    } : undefined,
  })

  const updateMut = useMutation({
    mutationFn: (d: { firstName: string; lastName: string }) =>
      profileApi.update({ firstName: d.firstName, lastName: d.lastName }),
    onSuccess: (updated) => {
      qc.invalidateQueries({ queryKey: ['profile'] })
      if (user && token) {
        setAuth({ ...user, firstName: updated.firstName, lastName: updated.lastName }, token)
      }
    },
  })

  // ── Password form ─────────────────────────────────────────────────────────
  const { register: regPwd, handleSubmit: hsPwd, reset: resetPwd, setError: setPwdError, formState: { errors: pwdErrors } } = useForm<{
    currentPassword: string; newPassword: string; confirmPassword: string
  }>()

  const pwdMut = useMutation({
    mutationFn: (d: { currentPassword: string; newPassword: string }) =>
      profileApi.changePassword(d.currentPassword, d.newPassword),
    onSuccess: () => { resetPwd(); setPwdSuccess(true); setTimeout(() => setPwdSuccess(false), 3000) },
    onError: () => setPwdError('currentPassword', { message: 'Неверный текущий пароль' }),
  })

  if (isLoading) {
    return (
      <div className="max-w-2xl mx-auto px-4 py-8">
        <div className="h-48 bg-gray-100 rounded-3xl animate-pulse" />
      </div>
    )
  }

  return (
    <div className="max-w-2xl mx-auto px-4 py-8">
      <h1 className="text-2xl font-bold text-gray-900 mb-6">Профиль</h1>

      {/* Avatar + roles */}
      <Card className="p-6 mb-5 flex items-center gap-5">
        <div className="w-20 h-20 rounded-full bg-primary-100 flex items-center justify-center text-primary-700 font-bold text-2xl shrink-0">
          {profile?.firstName[0]}{profile?.lastName[0]}
        </div>
        <div>
          <p className="text-xl font-semibold text-gray-900">{profile?.firstName} {profile?.lastName}</p>
          <p className="text-sm text-gray-500 mt-0.5">{profile?.phone}{profile?.email ? ` · ${profile.email}` : ''}</p>
          <div className="flex flex-wrap gap-1.5 mt-2">
            {profile?.roles.map(r => (
              <span key={r} className="text-xs bg-primary-50 text-primary-700 px-2 py-0.5 rounded-full font-medium">
                {roleLabel[r] ?? r}
              </span>
            ))}
          </div>
        </div>
      </Card>

      {/* Profile data */}
      <Card className="p-6 mb-5">
        <h2 className="font-semibold text-gray-900 mb-4">Личные данные</h2>
        <form onSubmit={hsProfile((d) => updateMut.mutate(d))} className="flex flex-col gap-4">
          <div className="grid grid-cols-2 gap-3">
            <Input label="Имя" {...regProfile('firstName', { required: true })} />
            <Input label="Фамилия" {...regProfile('lastName', { required: true })} />
          </div>
          {isMasterOrOwner && (
            <div className="flex flex-col gap-1">
              <label className="text-sm font-medium text-gray-700">Процент от услуги (%)</label>
              <div className="rounded-xl border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-600">
                {profile?.commissionPercent}%
              </div>
              <p className="text-xs text-gray-400">Устанавливается владельцем компании, самостоятельно изменить нельзя</p>
            </div>
          )}
          {updateMut.isSuccess && <p className="text-sm text-green-600">✓ Данные сохранены</p>}
          <Button type="submit" loading={updateMut.isPending} disabled={!pDirty}>
            Сохранить изменения
          </Button>
        </form>
      </Card>

      {profile?.plan && <PlanSection plan={profile.plan} />}

      {/* Password */}
      <Card className="p-6">
        <h2 className="font-semibold text-gray-900 mb-4">Смена пароля</h2>
        <form onSubmit={hsPwd((d) => {
          if (d.newPassword !== d.confirmPassword) {
            setPwdError('confirmPassword', { message: 'Пароли не совпадают' })
            return
          }
          pwdMut.mutate({ currentPassword: d.currentPassword, newPassword: d.newPassword })
        })} className="flex flex-col gap-4">
          <Input
            label="Текущий пароль"
            type="password"
            error={pwdErrors.currentPassword?.message}
            {...regPwd('currentPassword', { required: true })}
          />
          <Input
            label="Новый пароль"
            type="password"
            {...regPwd('newPassword', { required: true, minLength: { value: 8, message: 'Минимум 8 символов' } })}
          />
          <Input
            label="Повторите новый пароль"
            type="password"
            error={pwdErrors.confirmPassword?.message}
            {...regPwd('confirmPassword', { required: true })}
          />
          {pwdSuccess && <p className="text-sm text-green-600">✓ Пароль изменён</p>}
          <Button type="submit" loading={pwdMut.isPending}>Изменить пароль</Button>
        </form>
      </Card>
    </div>
  )
}
