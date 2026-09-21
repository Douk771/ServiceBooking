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
import { isRussianPhone } from '../utils/phone'
import { useState } from 'react'

interface FormData {
  firstName: string
  lastName: string
  phone: string
  password: string
  email?: string
  acceptedLegal: boolean
}

export function RegisterPage() {
  const {
    register,
    handleSubmit,
    watch,
    control,
    formState: { errors },
  } = useForm<FormData>({
    defaultValues: { acceptedLegal: false, phone: '' },
  })
  const { setAuth } = useAuthStore()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const acceptedLegal = watch('acceptedLegal')

  const onSubmit = async (data: FormData) => {
    setLoading(true)
    setError('')
    try {
      const res = await authApi.register({ ...data, email: data.email || undefined })
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
      setError(getAuthErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-[calc(100vh-76px)] flex items-center justify-center px-4 py-16">
      <div className="w-full max-w-[440px]">
        <Link to="/" className="flex items-center justify-center gap-3 mb-10">
          <span className="w-[38px] h-[38px] rounded-full bg-ink flex items-center justify-center shrink-0">
            <Icon name="calendar" size={18} className="text-cream" strokeWidth={1.6} />
          </span>
          <span className="font-serif text-xl text-ink">EZBOOK</span>
        </Link>

        <div className="bg-white border border-line rounded-3xl p-11 shadow-soft">
          <div className="text-center mb-8">
            <h1 className="font-serif text-[28px] font-medium text-ink mb-2">Создать аккаунт</h1>
            <p className="text-sm text-ink-soft">Присоединяйтесь — это бесплатно</p>
          </div>

          <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-[18px]">
            <div className="grid grid-cols-2 gap-3.5">
              <Input
                label="Имя"
                placeholder="Иван"
                error={errors.firstName?.message}
                {...register('firstName', { required: 'Введите имя' })}
              />
              <Input
                label="Фамилия"
                placeholder="Иванов"
                error={errors.lastName?.message}
                {...register('lastName', { required: 'Введите фамилию' })}
              />
            </div>
            <Controller
              name="phone"
              control={control}
              rules={{
                required: 'Введите телефон',
                validate: (v) =>
                  isRussianPhone(v) || 'Пока принимаем только российские номера, в формате +7 (900) 000-00-00',
              }}
              render={({ field }) => (
                <PhoneInput
                  label="Телефон"
                  error={errors.phone?.message}
                  value={field.value}
                  onChange={field.onChange}
                />
              )}
            />
            <Input label="Email (необязательно)" type="email" placeholder="your@email.com" {...register('email')} />
            <div className="flex flex-col gap-1.5">
              <Input
                label="Пароль"
                type="password"
                placeholder="••••••••"
                error={errors.password?.message}
                {...register('password', {
                  required: 'Введите пароль',
                  minLength: { value: 8, message: 'Пароль должен быть не короче 8 символов' },
                  validate: {
                    hasLower: (v) => /[a-z]/.test(v) || 'Добавьте строчную букву',
                    hasUpper: (v) => /[A-Z]/.test(v) || 'Добавьте заглавную букву',
                    hasDigit: (v) => /\d/.test(v) || 'Добавьте цифру',
                  },
                })}
              />
              {/* US-60/Q6 (ARCHITECTURE_CYCLE6.md §42.3, API_CONTRACT_CYCLE6.md §39.6): the password
                  policy itself is not changed by the customer's decision — only made visible up
                  front, so the user doesn't have to guess it from a rejected-array error after the
                  fact. */}
              <p className="text-xs text-muted">
                Не менее 8 символов, хотя бы одна строчная и одна заглавная буква, хотя бы одна цифра
              </p>
            </div>

            <label className="flex items-start gap-2.5 cursor-pointer">
              <input
                type="checkbox"
                className="w-4 h-4 mt-0.5 rounded accent-gold"
                {...register('acceptedLegal', { required: true })}
              />
              <span className="text-[13px] text-ink-soft leading-snug">
                Принимаю{' '}
                <Link to="/terms" target="_blank" className="text-gold hover:text-gold-dark">
                  пользовательское соглашение
                </Link>{' '}
                и{' '}
                <Link to="/privacy" target="_blank" className="text-gold hover:text-gold-dark">
                  политику обработки персональных данных
                </Link>
              </span>
            </label>

            {/* US-33 п. 1 */}
            <p className="text-[13px] text-muted -mt-2.5">
              Оставляя номер телефона, вы получите сервисные сообщения о своих записях в WhatsApp от салонов.
            </p>

            {error && <div className="bg-danger-bg text-danger text-sm px-4 py-2 rounded-xl">{error}</div>}

            <Button type="submit" size="lg" loading={loading} disabled={!acceptedLegal} className="mt-1 w-full">
              Зарегистрироваться
            </Button>
          </form>

          <p className="text-center text-sm text-ink-soft mt-7">
            Уже есть аккаунт?{' '}
            <Link to="/login" className="text-gold font-semibold hover:text-gold-dark">
              Войти
            </Link>
          </p>
        </div>
      </div>
    </div>
  )
}
