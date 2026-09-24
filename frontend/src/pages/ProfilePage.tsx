import { useEffect, useRef, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm, Controller } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { AxiosError } from 'axios'
import { format } from 'date-fns'
import { profileApi, type ProfilePlanDto, type ProfileDto } from '../api/profile'
import { notificationsApi } from '../api/notifications'
import { useAuthStore } from '../store/authStore'
import { useExportData } from '../hooks/useExportData'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { Icon } from '../components/ui/Icon'
import { Avatar } from '../components/ui/Avatar'
import { PhoneInput } from '../components/ui/PhoneInput'
import { formatPhone, isRussianPhone } from '../utils/phone'
import { getUploadErrorMessage } from '../utils/uploadError'
import { isPhoneChangeVerificationRequired, isPhoneChangeVerificationUnavailable } from '../utils/phoneVerificationError'
import { usePhoneVerificationConfig } from '../hooks/usePhoneVerification'
import { VerifyPhoneButton, type PhoneVerificationRefValue } from '../components/phoneVerification/VerifyPhoneButton'
import { PhoneVerifiedBadge } from '../components/phoneVerification/PhoneVerifiedBadge'

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

/**
 * US-12-14, T12-F6 — reused as-is; `phoneVerified` is written directly by the webhook handler once
 * the Profile-purpose session is redeemed (§148.3), so there's no separate "confirm the session"
 * call here: verifying just means refetching `profile` once the dialog reports `Verified`.
 *
 * §149.1 — three states, no fourth: verified (badge + date), unverified with the subsystem on
 * (neutral offer), unverified with the subsystem off (renders nothing — the screen must look exactly
 * like it did before this cycle).
 */
function PhoneVerificationNotice({ profile, onVerified }: { profile: ProfileDto; onVerified: () => void }) {
  const { data: config } = usePhoneVerificationConfig()

  if (profile.phoneVerified) {
    return <PhoneVerifiedBadge verifiedAtUtc={profile.phoneVerifiedAtUtc} className="mt-2" />
  }
  if (!config?.enabled || !config.healthy) return null

  return (
    <div className="mt-2">
      <p className="text-xs text-ink-soft mb-1.5">Номер ещё не подтверждён — это не обязательно, но открывает больше возможностей.</p>
      <VerifyPhoneButton phone={profile.phone} onVerifiedChange={(ref) => ref && onVerified()} />
    </div>
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
    control: phoneControl,
    watch: watchPhone,
    formState: { errors: phoneErrors },
  } = useForm<{
    currentPassword: string
    newPhone: string
  }>({ defaultValues: { newPhone: '' } })
  const newPhoneValue = watchPhone('newPhone')

  // US-12-17 (Р3 + Р6) — the new 409 the gate can return: the number already has guest bookings on
  // it and needs a verified MAX session before the change can go through. `changePhoneVerification`
  // is presented once and then cleared on success/failure — it is never persisted across page loads.
  const [needsPhoneVerification, setNeedsPhoneVerification] = useState(false)
  const [phoneVerificationUnavailable, setPhoneVerificationUnavailable] = useState(false)
  const [changePhoneVerification, setChangePhoneVerification] = useState<PhoneVerificationRefValue | null>(null)

  // Editing the number again after a 409 is a fresh attempt — the previous gate result no longer
  // says anything about whatever's now in the field. This also unmounts `VerifyPhoneButton` (it only
  // renders while `needsPhoneVerification` is true), and an unmount doesn't run its "drop a
  // now-mismatched session" effect — so `changePhoneVerification` must be cleared here explicitly,
  // or a stale session ref for the OLD number could still ride along on submit.
  useEffect(() => {
    setNeedsPhoneVerification(false)
    setPhoneVerificationUnavailable(false)
    setChangePhoneVerification(null)
  }, [newPhoneValue])

  const phoneMut = useMutation({
    mutationFn: (d: { currentPassword: string; newPhone: string; verification?: PhoneVerificationRefValue }) =>
      profileApi.changePhone(d.currentPassword, d.newPhone, d.verification ?? undefined),
    onSuccess: (updated) => {
      qc.invalidateQueries({ queryKey: ['profile'] })
      if (user && token) setAuth({ ...user, phone: updated.phone }, token)
      resetPhone()
      setNeedsPhoneVerification(false)
      setPhoneVerificationUnavailable(false)
      setChangePhoneVerification(null)
      setPhoneSuccess(true)
      setTimeout(() => setPhoneSuccess(false), 3000)
    },
    onError: (err: unknown) => {
      // US-12-17 (§169) — the two new 409 wordings are told apart by substring (same convention as
      // `utils/authError.ts`'s two meanings of 409 on /auth/register), and drive two different UIs
      // below the form rather than just an inline field error.
      if (isPhoneChangeVerificationRequired(err)) {
        setNeedsPhoneVerification(true)
        setPhoneVerificationUnavailable(false)
        setPhoneError('newPhone', { message: 'На этом номере уже есть записи. Подтвердите его через MAX ниже.' })
        return
      }
      if (isPhoneChangeVerificationUnavailable(err)) {
        setNeedsPhoneVerification(false)
        setPhoneVerificationUnavailable(true)
        setPhoneError('newPhone', { message: 'Сейчас сменить номер на этот нельзя: подтверждение номера на платформе пока не работает.' })
        return
      }
      setNeedsPhoneVerification(false)
      setPhoneVerificationUnavailable(false)
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
          {profile && (
            <PhoneVerificationNotice profile={profile} onVerified={() => qc.invalidateQueries({ queryKey: ['profile'] })} />
          )}
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
        <form
          onSubmit={hsPhone((d) => phoneMut.mutate({ ...d, verification: changePhoneVerification ?? undefined }))}
          className="flex flex-col gap-4"
        >
          <Controller
            name="newPhone"
            control={phoneControl}
            rules={{
              required: 'Введите телефон',
              validate: (v) =>
                isRussianPhone(v) || 'Пока принимаем только российские номера, в формате +7 (900) 000-00-00',
            }}
            render={({ field }) => (
              <PhoneInput
                label="Новый телефон"
                error={phoneErrors.newPhone?.message}
                value={field.value}
                onChange={field.onChange}
              />
            )}
          />
          <Input
            label="Текущий пароль"
            type="password"
            error={phoneErrors.currentPassword?.message}
            {...regPhone('currentPassword', { required: true })}
          />
          {/* US-12-17 (§169) — this number already has guest bookings on it: offer the same MAX
              verification widget, scoped to the number just typed above. §169's OTHER 409 (subsystem
              off) gets no such offer — there is nothing to click that would actually work. */}
          {needsPhoneVerification && (
            <div>
              <VerifyPhoneButton phone={newPhoneValue} onVerifiedChange={setChangePhoneVerification} />
            </div>
          )}
          {phoneVerificationUnavailable && (
            <p className="text-xs text-muted">
              Сменить номер на этот можно будет, когда на платформе снова заработает подтверждение телефона.
            </p>
          )}
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

      {/* Consents (US-68, T5-F3) */}
      <Card className="p-[26px] mt-[18px]">
        <h2 className="text-[15.5px] font-semibold text-ink mb-2">Мои согласия</h2>
        <p className="text-sm text-ink-soft mb-4">
          Согласие на обработку персональных данных — отдельный документ. Посмотреть, что вы отмечали, и отозвать
          любую цель можно на отдельной странице.
        </p>
        <Link to="/profile/consents">
          <Button variant="secondary">Мои согласия</Button>
        </Link>
      </Card>

      {/* Data export (US-38) */}
      <Card className="p-[26px] mt-[18px]">
        <h2 className="text-[15.5px] font-semibold text-ink mb-3">Мои данные</h2>
        <p className="text-sm text-ink-soft mb-1.5">
          В файл войдут: профиль, история согласий, компании, где вы состоите, ваши записи и отзывы, журнал
          отправленных вам уведомлений, статус отписки, а также перечень заметок и фотографий о вас (без содержимого).
        </p>
        <p className="text-sm text-ink-soft mb-4">
          Текст заметок сотрудников салона о вас и содержимое фотографий, загруженных салоном, ведёт сама компания —
          она отдельный оператор этих данных. В файле вы найдёте перечень таких компаний с их контактами: запрос об
          этих данных направляйте напрямую им.
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
