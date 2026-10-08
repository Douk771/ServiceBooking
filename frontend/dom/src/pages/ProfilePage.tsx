import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { profileApi } from '@/api/profile'
import { useAuthStore } from '@/store/authStore'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input } from '@/components/ui/Input'
import { formatPhone } from '@/utils/phone'
import { usePhoneVerificationConfig } from '@/hooks/usePhoneVerification'
import { VerifyPhoneButton } from '@/components/phoneVerification/VerifyPhoneButton'
import { PhoneVerifiedBadge } from '@/components/phoneVerification/PhoneVerifiedBadge'
import { unsubscribeCurrentDeviceOnLogout } from '@/hooks/useWebPush'
import { DevicesAndNotificationsSection } from '@/components/push/DevicesAndNotificationsSection'
import { StaffMaxCard } from '@/components/staffMax/StaffMaxCard'
import { staysCompaniesApi } from '../api/staysCompanies'
import { ErrorState, InlineError, LoadingList, Skeleton } from '../components/StatePanels'
import { getStayErrorMessage } from '../utils/stayError'

/**
 * `/profile` (P1, US-37-22/23) — minimal profile: name, phone with confirmation, devices and notifications of staff, «Мои согласия»,
 * «Уведомления платформы», sign-out. Data export and account deletion live on ezbook.ru (the account is shared); the link comes from `kinds-summary`.
 */
export function ProfilePage() {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const { user, token, setAuth, logout } = useAuthStore()
  const { data: profile, isLoading, isError, error, refetch } = useQuery({ queryKey: ['profile'], queryFn: profileApi.get })
  const { data: summary } = useQuery({ queryKey: ['kinds-summary'], queryFn: staysCompaniesApi.kindsSummary, retry: false })
  // «Заказы в MAX» (US-39-21): for the owner and the manager of any company of the vertical; a housekeeper gets no card (ARCHITECTURE_CYCLE39.md §39.15.1).
  const { data: myCompanies } = useQuery({ queryKey: ['stays-companies-my'], queryFn: staysCompaniesApi.my, retry: false })
  const showMax = (myCompanies ?? []).some((c) => c.myRole === 'Owner' || c.myRole === 'Manager')
  const { data: verifyConfig } = usePhoneVerificationConfig()
  const [editing, setEditing] = useState(false)
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')

  const save = useMutation({
    mutationFn: () => profileApi.update({ firstName: firstName.trim(), lastName: lastName.trim() }),
    onSuccess: (p) => {
      if (user && token) setAuth({ ...user, firstName: p.firstName, lastName: p.lastName }, token)
      void qc.invalidateQueries({ queryKey: ['profile'] })
      setEditing(false)
    },
  })

  if (isLoading)
    return (
      <main className="mx-auto max-w-[640px] px-4 pt-10">
        <Skeleton className="h-64" />
      </main>
    )
  if (isError || !profile)
    return (
      <main className="mx-auto max-w-[640px] px-4 pt-10">
        <ErrorState message={getStayErrorMessage(error, 'Не удалось загрузить профиль.')} onRetry={() => void refetch()} />
      </main>
    )

  return (
    <main className="mx-auto max-w-[640px] px-4 pt-10 sm:px-8">
      <h1 className="mb-7 font-serif text-[32px] text-ink">Профиль</h1>

      <Card className="mb-4 p-6">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-[15px] font-semibold text-ink">Имя</h2>
          {!editing && (
            <Button
              variant="ghost"
              size="sm"
              className="min-h-[44px]"
              onClick={() => {
                setFirstName(profile.firstName)
                setLastName(profile.lastName)
                setEditing(true)
              }}
            >
              Изменить
            </Button>
          )}
        </div>
        {editing ? (
          <form
            className="flex flex-col gap-3"
            onSubmit={(e) => {
              e.preventDefault()
              if (firstName.trim() && lastName.trim()) save.mutate()
            }}
          >
            <Input label="Имя" value={firstName} onChange={(e) => setFirstName(e.target.value)} maxLength={100} />
            <Input label="Фамилия" value={lastName} onChange={(e) => setLastName(e.target.value)} maxLength={100} />
            {save.isError && <p className="text-sm text-danger">{getStayErrorMessage(save.error, 'Не удалось сохранить имя.')}</p>}
            <div className="flex gap-3">
              <Button type="button" variant="secondary" className="min-h-[44px]" onClick={() => setEditing(false)}>
                Отмена
              </Button>
              <Button type="submit" className="min-h-[44px]" loading={save.isPending} disabled={!firstName.trim() || !lastName.trim()}>
                Сохранить
              </Button>
            </div>
          </form>
        ) : (
          <p className="text-ink">
            {profile.firstName} {profile.lastName}
          </p>
        )}
      </Card>

      <Card className="mb-4 p-6">
        <h2 className="mb-3 text-[15px] font-semibold text-ink">Телефон</h2>
        <p className="text-ink">{formatPhone(profile.phone)}</p>
        {profile.phoneVerified ? (
          <PhoneVerifiedBadge verifiedAtUtc={profile.phoneVerifiedAtUtc} className="mt-3" />
        ) : (
          verifyConfig?.enabled && (
            <div className="mt-3">
              <p className="mb-2 text-sm text-ink-soft">Подтверждённый номер нужен для некоторых сервисов платформы.</p>
              <VerifyPhoneButton phone={profile.phone} onVerifiedChange={(ref) => ref && void qc.invalidateQueries({ queryKey: ['profile'] })} />
            </div>
          )
        )}
      </Card>

      <DevicesAndNotificationsSection site="Stays" appName="Дома" keepBrowserSubscription className="mb-4" />

      {showMax && (
        <div className="-mt-2 mb-4">
          <StaffMaxCard getErrorMessage={getStayErrorMessage} slots={{ LoadingList, ErrorState, InlineError }} />
        </div>
      )}

      <Card className="mb-4 p-6">
        <h2 className="mb-3 text-[15px] font-semibold text-ink">Данные и согласия</h2>
        <ul className="flex flex-col gap-2 text-sm">
          <li>
            <Link to="/profile/consents" className="font-medium text-gold hover:text-gold-dark">
              Мои согласия
            </Link>
          </li>
          <li>
            <Link to="/notices" className="font-medium text-gold hover:text-gold-dark">
              Уведомления платформы
            </Link>
          </li>
          <li className="text-ink-soft">
            Аккаунт общий с ezbook.ru: скачать свои данные или удалить аккаунт можно в профиле на{' '}
            {summary?.services.siteUrl ? (
              <a href={`${summary.services.siteUrl}/profile`} className="font-medium text-gold hover:text-gold-dark">
                ezbook.ru
              </a>
            ) : (
              'ezbook.ru'
            )}
            .
          </li>
        </ul>
      </Card>

      <Button
        variant="secondary"
        className="min-h-[44px]"
        onClick={async () => {
          // The staff push row of this browser goes before the token does (the guest role of the browser keeps its subscription).
          await unsubscribeCurrentDeviceOnLogout({ keepBrowserSubscription: true }).catch(() => undefined)
          logout()
          qc.clear()
          navigate('/')
        }}
      >
        Выйти
      </Button>
    </main>
  )
}
