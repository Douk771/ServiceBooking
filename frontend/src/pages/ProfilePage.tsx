import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { AxiosError } from 'axios'
import { format } from 'date-fns'
import { profileApi, type ProfilePlanDto } from '../api/profile'
import { useAuthStore } from '../store/authStore'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { Icon } from '../components/ui/Icon'

const roleLabel: Record<string, string> = {
  Client: 'Клиент', Master: 'Мастер', CompanyOwner: 'Владелец', SuperAdmin: 'Супер-администратор',
}

function PlanFeature({ label, enabled }: { label: string; enabled: boolean }) {
  return (
    <span className={`inline-flex items-center gap-1 text-xs font-semibold px-2.5 py-1 rounded-full ${
      enabled ? 'bg-success-bg text-success' : 'bg-[#F5F2EC] text-muted'
    }`}>
      {enabled ? '✓' : '✗'} {label}
    </span>
  )
}

function PlanSection({ plan }: { plan: ProfilePlanDto }) {
  const statusLabel = !plan.isActive
    ? { text: 'Отключена', className: 'bg-[#F5F2EC] text-muted' }
    : plan.isExpired
      ? { text: 'Истекла', className: 'bg-danger-bg text-danger' }
      : { text: 'Активна', className: 'bg-success-bg text-success' }

  return (
    <Card className="p-[26px] mb-[18px]">
      <div className="flex items-center justify-between mb-3.5 flex-wrap gap-2">
        <h2 className="text-[15.5px] font-semibold text-ink">Тарифный план</h2>
        <span className={`text-xs font-semibold px-2.5 py-1 rounded-full ${statusLabel.className}`}>{statusLabel.text}</span>
      </div>
      <div className="flex items-center gap-3.5 mb-3.5 flex-wrap">
        <span className="text-lg font-bold text-ink">{plan.planName}</span>
        <span className="text-sm font-semibold text-gold-dark">
          {plan.pricePerMonth > 0 ? `${plan.pricePerMonth.toLocaleString('ru-RU')} ₽/мес` : 'Бесплатно'}
        </span>
        {plan.paidUntil && (
          <span className="text-[12.5px] text-muted">
            {plan.isExpired ? 'Истёк' : 'Оплачен до'} {format(new Date(plan.paidUntil), 'd MMM yyyy')}
          </span>
        )}
      </div>
      <div className="flex flex-wrap gap-2 mb-3">
        <PlanFeature label="Онлайн-запись" enabled={plan.allowOnlineBooking} />
        <PlanFeature label="Рассылка" enabled={plan.allowMailing} />
        <PlanFeature label="Аналитика" enabled={plan.allowAnalytics} />
      </div>
      <div className="flex gap-4 text-xs text-muted">
        <span>Сотрудников: {plan.maxEmployees !== null ? `до ${plan.maxEmployees}` : '∞'}</span>
        <span>Компаний: {plan.maxCompanies !== null ? `до ${plan.maxCompanies}` : '∞'}</span>
      </div>
      <p className="text-xs text-muted mt-3">Изменение тарифа и оплата скоро будут доступны здесь же.</p>
    </Card>
  )
}

export function ProfilePage() {
  const { user, setAuth, token } = useAuthStore()
  const qc = useQueryClient()
  const [pwdSuccess, setPwdSuccess] = useState(false)
  const [phoneSuccess, setPhoneSuccess] = useState(false)

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

  // ── Phone form ────────────────────────────────────────────────────────────
  const { register: regPhone, handleSubmit: hsPhone, reset: resetPhone, setError: setPhoneError, formState: { errors: phoneErrors } } = useForm<{
    currentPassword: string; newPhone: string
  }>()

  const phoneMut = useMutation({
    mutationFn: (d: { currentPassword: string; newPhone: string }) =>
      profileApi.changePhone(d.currentPassword, d.newPhone),
    onSuccess: (updated) => {
      qc.invalidateQueries({ queryKey: ['profile'] })
      if (user && token) setAuth({ ...user, phone: updated.phone }, token)
      resetPhone()
      setPhoneSuccess(true)
      setTimeout(() => setPhoneSuccess(false), 3000)
    },
    onError: (err: unknown) => {
      const message = err instanceof AxiosError && typeof err.response?.data === 'string'
        ? err.response.data
        : 'Не удалось изменить номер телефона'
      setPhoneError('currentPassword', { message })
    },
  })

  if (isLoading) {
    return (
      <div className="max-w-[640px] mx-auto px-8 py-11">
        <div className="h-48 bg-cream-deep rounded-3xl animate-pulse" />
      </div>
    )
  }

  return (
    <div className="max-w-[640px] mx-auto px-8 pt-11 pb-24">
      <h1 className="font-serif text-[30px] font-medium text-ink mb-7">Профиль</h1>

      {/* Avatar + roles */}
      <Card className="p-[26px] mb-[18px] flex items-center gap-5">
        <div className="w-[72px] h-[72px] rounded-full bg-cream-deep flex items-center justify-center text-gold-dark font-bold text-2xl shrink-0">
          {profile?.firstName[0]}{profile?.lastName[0]}
        </div>
        <div>
          <p className="text-[19px] font-semibold text-ink">{profile?.firstName} {profile?.lastName}</p>
          <p className="text-[13.5px] text-ink-soft mt-1">{profile?.phone}{profile?.email ? ` · ${profile.email}` : ''}</p>
          <div className="flex flex-wrap gap-1.5 mt-2.5">
            {profile?.roles.map(r => (
              <span key={r} className="text-xs font-semibold bg-cream-deep text-gold-dark px-2.5 py-1 rounded-full">
                {roleLabel[r] ?? r}
              </span>
            ))}
          </div>
        </div>
      </Card>

      {/* Profile data */}
      <Card className="p-[26px] mb-[18px]">
        <h2 className="text-[15.5px] font-semibold text-ink mb-[18px]">Личные данные</h2>
        <form onSubmit={hsProfile((d) => updateMut.mutate(d))} className="flex flex-col gap-4">
          <div className="grid grid-cols-2 gap-3.5">
            <Input label="Имя" {...regProfile('firstName', { required: true })} />
            <Input label="Фамилия" {...regProfile('lastName', { required: true })} />
          </div>
          {isMasterOrOwner && (
            <div className="flex flex-col gap-1.5">
              <label className="text-[13px] font-medium text-[#4A4038]">Процент от услуги (%)</label>
              <div className="rounded-xl border border-line bg-cream-deep px-3.5 py-2.5 text-sm text-ink-soft">
                {profile?.commissionPercent}%
              </div>
              <p className="text-xs text-muted">Устанавливается владельцем компании, самостоятельно изменить нельзя</p>
            </div>
          )}
          {updateMut.isSuccess && (
            <p className="text-sm text-success flex items-center gap-1.5">
              <Icon name="check" size={14} strokeWidth={2} /> Данные сохранены
            </p>
          )}
          <Button type="submit" loading={updateMut.isPending} disabled={!pDirty}>
            Сохранить изменения
          </Button>
        </form>
      </Card>

      {profile?.plan && <PlanSection plan={profile.plan} />}

      {/* Password */}
      <Card className="p-[26px]">
        <h2 className="text-[15.5px] font-semibold text-ink mb-[18px]">Смена пароля</h2>
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
          {pwdSuccess && (
            <p className="text-sm text-success flex items-center gap-1.5">
              <Icon name="check" size={14} strokeWidth={2} /> Пароль изменён
            </p>
          )}
          <Button type="submit" variant="secondary" loading={pwdMut.isPending}>Изменить пароль</Button>
        </form>
      </Card>

      {/* Phone */}
      <Card className="p-[26px] mt-[18px]">
        <h2 className="text-[15.5px] font-semibold text-ink mb-[18px]">Смена телефона</h2>
        <form onSubmit={hsPhone((d) => phoneMut.mutate(d))} className="flex flex-col gap-4">
          <Input
            label="Новый телефон"
            type="tel"
            placeholder="+7 999 000 00 00"
            error={phoneErrors.newPhone?.message}
            {...regPhone('newPhone', { required: 'Введите телефон' })}
          />
          <Input
            label="Текущий пароль"
            type="password"
            error={phoneErrors.currentPassword?.message}
            {...regPhone('currentPassword', { required: true })}
          />
          {phoneSuccess && (
            <p className="text-sm text-success flex items-center gap-1.5">
              <Icon name="check" size={14} strokeWidth={2} /> Телефон изменён
            </p>
          )}
          <Button type="submit" variant="secondary" loading={phoneMut.isPending}>Изменить телефон</Button>
        </form>
      </Card>
    </div>
  )
}
