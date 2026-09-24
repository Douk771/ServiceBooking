import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, Link } from 'react-router-dom'
import { AxiosError } from 'axios'
import { profileApi } from '../api/profile'
import { useAuthStore } from '../store/authStore'
import { GuestDataGateNotice } from '../components/profile/GuestDataGateNotice'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { Icon } from '../components/ui/Icon'

/**
 * US-39. POST /api/profile/delete-account is in the 451 allow-list (API_CONTRACT.md §0.4) — this page
 * must stay reachable even for a user who hasn't accepted the latest legal revision yet, which is why
 * ConsentGate (T-F2) explicitly links here instead of blocking it.
 */
export function DeleteAccountPage() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { logout } = useAuthStore()
  // Shared cache with ProfilePage's ['profile'] query (§276.1) — no second, gate-specific fetch.
  const { data: profile } = useQuery({ queryKey: ['profile'], queryFn: profileApi.get })
  const [password, setPassword] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [error, setError] = useState('')

  const mut = useMutation({
    mutationFn: () => profileApi.deleteAccount(password),
    onSuccess: () => {
      logout()
      qc.clear()
      navigate('/')
    },
    onError: (err: unknown) => {
      const message =
        err instanceof AxiosError && typeof err.response?.data === 'string'
          ? err.response.data
          : 'Не удалось удалить аккаунт. Попробуйте снова.'
      setError(message)
    },
  })

  return (
    <div className="max-w-[560px] mx-auto px-8 pt-11 pb-24">
      <h1 className="font-serif text-[30px] font-medium text-ink mb-6">Удаление аккаунта</h1>

      <Card className="p-[26px] flex flex-col gap-5">
        <div className="text-sm text-ink-soft leading-[1.6] flex flex-col gap-2.5">
          <p className="font-semibold text-ink">Это действие необратимо. При удалении:</p>
          <ul className="list-disc pl-5 flex flex-col gap-1.5">
            <li>сотрутся имя, телефон, email, аватар и пароль — войти в аккаунт станет невозможно;</li>
            <li>удалятся заметки о вас, загруженные о вас фотографии и поле «Противопоказания»;</li>
            <li>отзывы останутся, но без указания вашего имени;</li>
            <li>
              <strong>визиты останутся у салона в обезличенном виде</strong> (дата, услуга, мастер, цена, статус — без
              вашего имени и телефона), чтобы не портить отчётность салона;
            </li>
            <li>телефон освободится — по нему можно будет зарегистрироваться заново.</li>
          </ul>
          <p>
            Если вы владеете компанией, сначала передайте её другому владельцу или обратитесь в поддержку — иначе
            удаление вернёт ошибку.
          </p>
          <p>
            Подробнее — в{' '}
            <Link to="/privacy" className="text-gold hover:text-gold-dark">
              политике обработки персональных данных
            </Link>
            .
          </p>
        </div>

        {profile && <GuestDataGateNotice phoneVerified={profile.phoneVerified} />}

        <Input label="Текущий пароль" type="password" value={password} onChange={(e) => setPassword(e.target.value)} />

        <label className="flex items-start gap-3 cursor-pointer">
          <input
            type="checkbox"
            checked={confirmed}
            onChange={(e) => setConfirmed(e.target.checked)}
            className="w-4 h-4 mt-0.5 rounded accent-gold"
          />
          <span className="text-sm text-ink-soft">Понимаю, что действие необратимо</span>
        </label>

        {error && (
          <div className="bg-danger-bg text-danger text-sm px-4 py-2 rounded-xl flex items-start gap-2">
            <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5" />
            <span>{error}</span>
          </div>
        )}

        <Button
          variant="danger"
          size="lg"
          loading={mut.isPending}
          disabled={!password || !confirmed}
          onClick={() => {
            setError('')
            mut.mutate()
          }}
        >
          Удалить аккаунт навсегда
        </Button>
      </Card>
    </div>
  )
}
