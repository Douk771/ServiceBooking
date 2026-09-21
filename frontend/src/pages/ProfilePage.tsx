import { useRef, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { AxiosError } from 'axios'
import { format } from 'date-fns'
import { profileApi, type ProfilePlanDto } from '../api/profile'
import { notificationsApi } from '../api/notifications'
import { useAuthStore } from '../store/authStore'
import { useExportData } from '../hooks/useExportData'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { Icon } from '../components/ui/Icon'
import { Avatar } from '../components/ui/Avatar'
import { formatPhone } from '../utils/phone'
import { getUploadErrorMessage } from '../utils/uploadError'

const roleLabel: Record<string, string> = {
  Client: 'Клиент',
  Master: 'Мастер',
  CompanyOwner: 'Владелец',
  SuperAdmin: 'Супер-администратор',
}

function PlanFeature({ label, enabled }: { label: string; enabled: boolean }) {
  return (
    <span
      className={`inline-flex items-center gap-1 text-xs font-semibold px-2.5 py-1 rounded-full ${
        enabled ? 'bg-success-bg text-success' : 'bg-[#F5F2EC] text-muted'
      }`}
    >
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
        <span className={`text-xs font-semibold px-2.5 py-1 rounded-full ${statusLabel.className}`}>
          {statusLabel.text}
        </span>
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
      <div className="flex gap-4 text-xs text-muted flex-wrap">
        <span>
          Сотрудников суммарно: {plan.employeesUsed ?? 0}
          {plan.maxEmployees !== null ? ` / ${plan.maxEmployees}` : ' / ∞'}
        </span>
        <span>
          Компаний: {plan.companiesUsed ?? 0}
          {plan.maxCompanies !== null ? ` / ${plan.maxCompanies}` : ' / ∞'}
        </span>
      </div>
      {plan.isExpiringSoon && (
        <p className="text-xs text-warning mt-3">
          {plan.expiresInDays != null
            ? `Подписка истекает через ${plan.expiresInDays} дн. — продлите её на странице «Ваша подписка».`
            : 'Подписка скоро истекает — продлите её на странице «Ваша подписка».'}
        </p>
      )}
      <Link
        to="/billing"
        className="inline-block text-xs font-semibold text-gold-dark mt-3 hover:underline"
      >
        Управлять подпиской →
      </Link>
    </Card>
  )
}

// US-33 п. 6 — client-side opt-out toggle, same effect as the unsubscribe-link page.
function NotificationPreferencesCard() {
  const qc = useQueryClient()
  const { data, isLoading } = useQuery({ queryKey: ['notification-preferences'], queryFn: notificationsApi.getPreferences })

  const mut = useMutation({
    mutationFn: (enabled: boolean) => notificationsApi.updatePreferences(enabled),
    onSuccess: (_res, enabled) => {
      qc.setQueryData(['notification-preferences'], { enabled })
    },
  })

  if (isLoading) return <div className="h-20 bg-cream-deep rounded-2xl animate-pulse mb-[18px]" />
  if (!data) return null

  return (
    <Card className="p-[26px] mb-[18px]">
      <div className="flex items-center justify-between gap-4 flex-wrap">
        <div>
          <h2 className="text-[15.5px] font-semibold text-ink mb-1">Уведомления о визитах</h2>
          <p className="text-sm text-ink-soft">
            Сервисные сообщения о записях — подтверждения, напоминания, отмены — приходят в WhatsApp от салона.
          </p>
        </div>
        <label className="flex items-center gap-2.5 cursor-pointer shrink-0">
          <span className="text-sm text-ink-soft">{data.enabled ? 'Включены' : 'Выключены'}</span>
          <input
            type="checkbox"
            className="w-4 h-4 accent-gold rounded"
            checked={data.enabled}
            disabled={mut.isPending}
            onChange={(e) => mut.mutate(e.target.checked)}
          />
        </label>
      </div>
      {mut.isError && <p className="text-sm text-danger mt-2">Не удалось сохранить настройку. Попробуйте снова.</p>}
    </Card>
  )
}

export function ProfilePage() {
  const { user, setAuth, token } = useAuthStore()
  const qc = useQueryClient()
  const [pwdSuccess, setPwdSuccess] = useState(false)
  const [phoneSuccess, setPhoneSuccess] = useState(false)
  const avatarInputRef = useRef<HTMLInputElement>(null)
  const [avatarError, setAvatarError] = useState('')

  const { data: profile, isLoading } = useQuery({
    queryKey: ['profile'],
    queryFn: profileApi.get,
  })

  // The avatar is shown from `profile.avatarUrl` (this page's own query) rather than from the auth
  // store's `user` — that type doesn't carry it, and the only place besides here that displays a
  // user's own avatar today is a future concern, not this one.
  const avatarMut = useMutation({
    mutationFn: (file: File) => profileApi.uploadAvatar(file),
    onMutate: () => setAvatarError(''),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['profile'] }),
    onError: (err: unknown) => setAvatarError(getUploadErrorMessage(err)),
  })

  // ── Profile form ──────────────────────────────────────────────────────────
  const {
    register: regProfile,
    handleSubmit: hsProfile,
    formState: { isDirty: pDirty },
  } = useForm({
    values: profile
      ? {
          firstName: profile.firstName,
          lastName: profile.lastName,
        }
      : undefined,
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
  const {
    register: regPwd,
    handleSubmit: hsPwd,
    reset: resetPwd,
    setError: setPwdError,
    formState: { errors: pwdErrors },
  } = useForm<{
    currentPassword: string
    newPassword: string
    confirmPassword: string
  }>()

  const pwdMut = useMutation({
    mutationFn: (d: { currentPassword: string; newPassword: string }) =>
      profileApi.changePassword(d.currentPassword, d.newPassword),
    onSuccess: () => {
      resetPwd()
      setPwdSuccess(true)
      setTimeout(() => setPwdSuccess(false), 3000)
    },
    onError: () => setPwdError('currentPassword', { message: 'Неверный текущий пароль' }),
  })

  // ── Phone form ────────────────────────────────────────────────────────────
  const {
    register: regPhone,
    handleSubmit: hsPhone,
    reset: resetPhone,
    setError: setPhoneError,
    formState: { errors: phoneErrors },
  } = useForm<{
    currentPassword: string
    newPhone: string
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
      const message =
        err instanceof AxiosError && typeof err.response?.data === 'string'
          ? err.response.data
          : 'Не удалось изменить номер телефона'
      setPhoneError('currentPassword', { message })
    },
  })

  // ── Data export (US-38) ──────────────────────────────────────────────────
  const { exportMut, exportError } = useExportData()

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
        <div className="relative shrink-0">
          {profile && (
            <Avatar
              avatarUrl={profile.avatarUrl}
              firstName={profile.firstName}
              lastName={profile.lastName}
              size={72}
              className="text-2xl"
            />
          )}
          <input
            ref={avatarInputRef}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            className="hidden"
            onChange={(e) => {
              const f = e.target.files?.[0]
              if (f) avatarMut.mutate(f)
              e.target.value = ''
            }}
          />
          <button
            type="button"
            onClick={() => avatarInputRef.current?.click()}
            aria-label="Изменить фото профиля"
            className="absolute -bottom-1 -right-1 w-6 h-6 rounded-full bg-ink text-cream flex items-center justify-center hover:bg-ink/90 transition-colors"
          >
            <Icon name="image" size={12} strokeWidth={1.8} />
          </button>
        </div>
        <div>
          <p className="text-[19px] font-semibold text-ink">
            {profile?.firstName} {profile?.lastName}
          </p>
          <p className="text-[13.5px] text-ink-soft mt-1">
            {formatPhone(profile?.phone)}
            {profile?.email ? ` · ${profile.email}` : ''}
          </p>
          <div className="flex flex-wrap gap-1.5 mt-2.5">
            {profile?.roles.map((r) => (
              <span key={r} className="text-xs font-semibold bg-cream-deep text-gold-dark px-2.5 py-1 rounded-full">
                {roleLabel[r] ?? r}
              </span>
            ))}
          </div>
          {avatarMut.isPending && <p className="text-xs text-muted mt-1.5">Загрузка фото…</p>}
          {avatarError && <p className="text-xs text-danger mt-1.5">{avatarError}</p>}
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

      <NotificationPreferencesCard />

      {/* Password */}
      <Card className="p-[26px]">
        <h2 className="text-[15.5px] font-semibold text-ink mb-[18px]">Смена пароля</h2>
        <form
          onSubmit={hsPwd((d) => {
            if (d.newPassword !== d.confirmPassword) {
              setPwdError('confirmPassword', { message: 'Пароли не совпадают' })
              return
            }
            pwdMut.mutate({ currentPassword: d.currentPassword, newPassword: d.newPassword })
          })}
          className="flex flex-col gap-4"
        >
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
          <Button type="submit" variant="secondary" loading={pwdMut.isPending}>
            Изменить пароль
          </Button>
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
          <Button type="submit" variant="secondary" loading={phoneMut.isPending}>
            Изменить телефон
          </Button>
        </form>
      </Card>

      {/* Data export (US-38) */}
      <Card className="p-[26px] mt-[18px]">
        <h2 className="text-[15.5px] font-semibold text-ink mb-3">Мои данные</h2>
        <p className="text-sm text-ink-soft mb-1.5">
          В файл войдут: профиль, история согласий, компании, где вы состоите, ваши записи и отзывы, а также перечень
          заметок и фотографий о вас (без содержимого).
        </p>
        <p className="text-sm text-ink-soft mb-4">
          В файл <strong>не войдут</strong>: текст заметок сотрудников салона о вас и содержимое фотографий, загруженных
          салоном, — это результат работы салона, а не ваши данные. Запросить их можно у салона напрямую.
        </p>
        {exportError && <p className="text-sm text-danger mb-3">{exportError}</p>}
        <Button variant="secondary" loading={exportMut.isPending} onClick={() => exportMut.mutate()}>
          Скачать мои данные
        </Button>
      </Card>

      {/* Account deletion (US-39) */}
      <Card className="p-[26px] mt-[18px]">
        <h2 className="text-[15.5px] font-semibold text-ink mb-2">Удаление аккаунта</h2>
        <p className="text-sm text-ink-soft mb-4">
          Удаление персональных данных и закрытие доступа. Действие необратимо.
        </p>
        <Link to="/profile/delete">
          <Button variant="danger">Удалить аккаунт</Button>
        </Link>
      </Card>
    </div>
  )
}
