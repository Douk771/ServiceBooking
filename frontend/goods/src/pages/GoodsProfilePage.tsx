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
import { shopsApi } from '../api/shops'
import { ErrorState, Skeleton } from '../components/StatePanels'
import { getGoodsErrorMessage } from '../utils/orderError'

/**
 * US-23-07 (P1) — minimal profile: name, phone with MAX confirmation, «Мои согласия», sign-out. Data export
 * and account deletion live on ezbook.ru (the account is shared); the link comes from `kinds-summary`.
 */
export function GoodsProfilePage() {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const { user, token, setAuth, logout } = useAuthStore()
  const { data: profile, isLoading, isError, error, refetch } = useQuery({ queryKey: ['profile'], queryFn: profileApi.get })
  const { data: summary } = useQuery({ queryKey: ['kinds-summary'], queryFn: shopsApi.kindsSummary, retry: false })
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
      <main className="max-w-[640px] mx-auto px-4 pt-10">
        <Skeleton className="h-64" />
      </main>
    )
  if (isError || !profile)
    return (
      <main className="max-w-[640px] mx-auto px-4 pt-10">
        <ErrorState message={getGoodsErrorMessage(error, 'Не удалось загрузить профиль.')} onRetry={() => void refetch()} />
      </main>
    )

  const startEdit = () => {
    setFirstName(profile.firstName)
    setLastName(profile.lastName)
    setEditing(true)
  }

  return (
    <main className="max-w-[640px] mx-auto px-4 sm:px-8 pt-10">
      <h1 className="font-serif text-[32px] text-ink mb-7">Профиль</h1>

      <Card className="p-6 mb-4">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-[15px] font-semibold text-ink">Имя</h2>
          {!editing && (
            <Button variant="ghost" size="sm" onClick={startEdit}>
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
            {save.isError && <p className="text-sm text-danger">{getGoodsErrorMessage(save.error, 'Не удалось сохранить имя.')}</p>}
            <div className="flex gap-3">
              <Button type="button" variant="secondary" onClick={() => setEditing(false)}>
                Отмена
              </Button>
              <Button type="submit" loading={save.isPending} disabled={!firstName.trim() || !lastName.trim()}>
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

      <Card className="p-6 mb-4">
        <h2 className="text-[15px] font-semibold text-ink mb-3">Телефон</h2>
        <p className="text-ink">{formatPhone(profile.phone)}</p>
        {profile.phoneVerified ? (
          <PhoneVerifiedBadge verifiedAtUtc={profile.phoneVerifiedAtUtc} className="mt-3" />
        ) : (
          verifyConfig?.enabled && (
            <div className="mt-3">
              <p className="text-sm text-ink-soft mb-2">
                Магазины с режимом «только с подтверждённым телефоном» принимают заказы только от подтверждённых номеров.
              </p>
              <VerifyPhoneButton phone={profile.phone} onVerifiedChange={(ref) => ref && void qc.invalidateQueries({ queryKey: ['profile'] })} />
            </div>
          )
        )}
      </Card>

      <Card className="p-6 mb-4">
        <h2 className="text-[15px] font-semibold text-ink mb-3">Данные и согласия</h2>
        <ul className="flex flex-col gap-2 text-sm">
          <li>
            <Link to="/profile/consents" className="text-gold hover:text-gold-dark font-medium">
              Мои согласия
            </Link>
          </li>
          <li className="text-ink-soft">
            Аккаунт общий с ezbook.ru: скачать свои данные или удалить аккаунт можно в профиле на{' '}
            {summary?.services.siteUrl ? (
              <a href={`${summary.services.siteUrl}/profile`} className="text-gold hover:text-gold-dark font-medium">
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
        onClick={() => {
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
