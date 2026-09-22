import { useForm, Controller } from 'react-hook-form'
import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { authApi } from '../api/auth'
import { useAuthStore } from '../store/authStore'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { PhoneInput } from '../components/ui/PhoneInput'
import { Icon } from '../components/ui/Icon'
import { getAuthErrorMessage } from '../utils/authError'
import { toCanonicalPhoneLenient } from '../utils/phone'
import { useState } from 'react'

interface FormData {
  phone: string
  password: string
}

export function LoginPage() {
  const {
    register,
    handleSubmit,
    control,
    formState: { errors },
  } = useForm<FormData>({ defaultValues: { phone: '' } })
  const { setAuth } = useAuthStore()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const onSubmit = async (data: FormData) => {
    setLoading(true)
    setError('')
    try {
      // §947: sign-in accepts a foreign number already on file (unlike registration); PhoneInput
      // here is in `restrictToRussia={false}` raw-text mode, so normalize once at submit time.
      const res = await authApi.login(toCanonicalPhoneLenient(data.phone), data.password)
      // Signing in over a live session (a direct /login link, or registering a second account without
      // logging out) would otherwise leave the previous user's cached queries in place, and the new
      // user gets a first frame of someone else's data. Navbar's logout clears for the same reason.
      qc.clear()
      setAuth(
        {
          id: res.userId,
          phone: res.phone,
          email: res.email,
          firstName: res.firstName,
          lastName: res.lastName,
          roles: res.roles,
        },
        res.token,
      )
      navigate('/')
    } catch (e: unknown) {
      // getAuthErrorMessage now tells 401 (wrong credentials), 423 (lockout), 403 (sign-in not
      // allowed), 429 (rate limit), 5xx and "no response" apart (ARCHITECTURE_CYCLE6.md §42.3.3) —
      // this screen used to collapse everything but 429 into "wrong phone or password".
      setError(getAuthErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-[calc(100vh-76px)] flex items-center justify-center px-4 py-16">
      <div className="w-full max-w-[420px]">
        <Link to="/" className="flex items-center justify-center gap-3 mb-10">
          <span className="w-[38px] h-[38px] rounded-full bg-ink flex items-center justify-center shrink-0">
            <Icon name="calendar" size={18} className="text-cream" strokeWidth={1.6} />
          </span>
          <span className="font-serif text-xl text-ink">EZBOOK</span>
        </Link>

        <div className="bg-white border border-line rounded-3xl p-11 shadow-soft">
          <div className="text-center mb-8">
            <h1 className="font-serif text-[28px] font-medium text-ink mb-2">С возвращением</h1>
            <p className="text-sm text-ink-soft">Войдите, чтобы управлять записями</p>
          </div>

          <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-[18px]">
            <Controller
              name="phone"
              control={control}
              rules={{ required: 'Введите телефон' }}
              render={({ field }) => (
                <PhoneInput
                  label="Телефон"
                  error={errors.phone?.message}
                  value={field.value}
                  onChange={field.onChange}
                  // Sign-in is exempt from the Russian-only policy (ARCHITECTURE_CYCLE6.md §947,
                  // §48.2): an account may already have a foreign number on file, and the server
                  // still normalizes logins with `Normalize`, not `TryNormalizeRussian`.
                  restrictToRussia={false}
                />
              )}
            />
            <Input
              label="Пароль"
              type="password"
              placeholder="••••••••"
              error={errors.password?.message}
              {...register('password', { required: 'Введите пароль' })}
            />

            {error && <div className="bg-danger-bg text-danger text-sm px-4 py-2 rounded-xl">{error}</div>}

            <Button type="submit" size="lg" loading={loading} className="mt-1 w-full">
              Войти
            </Button>
          </form>

          <p className="text-center text-sm text-ink-soft mt-7">
            Нет аккаунта?{' '}
            <Link to="/register" className="text-gold font-semibold hover:text-gold-dark">
              Зарегистрироваться
            </Link>
          </p>
        </div>
      </div>
    </div>
  )
}
