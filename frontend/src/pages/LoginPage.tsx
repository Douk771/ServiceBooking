import { useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router-dom'
import { authApi } from '../api/auth'
import { useAuthStore } from '../store/authStore'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { Icon } from '../components/ui/Icon'
import { useState } from 'react'

interface FormData {
  phone: string
  password: string
}

export function LoginPage() {
  const { register, handleSubmit, formState: { errors } } = useForm<FormData>()
  const { setAuth } = useAuthStore()
  const navigate = useNavigate()
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const onSubmit = async (data: FormData) => {
    setLoading(true)
    setError('')
    try {
      const res = await authApi.login(data.phone, data.password)
      setAuth({ id: res.userId, phone: res.phone, email: res.email, firstName: res.firstName, lastName: res.lastName, roles: res.roles }, res.token)
      navigate('/')
    } catch {
      setError('Неверный телефон или пароль')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-[calc(100vh-76px)] flex items-center justify-center px-4 py-16">
      <div className="w-full max-w-[420px]">
        <Link to="/" className="flex items-center justify-center gap-3 mb-10">
          <span className="w-[38px] h-[38px] rounded-full bg-ink flex items-center justify-center shrink-0">
            <Icon name="scissors" size={18} className="text-cream" strokeWidth={1.6} />
          </span>
          <span className="font-serif text-xl text-ink">EZBOOK</span>
        </Link>

        <div className="bg-white border border-line rounded-3xl p-11 shadow-soft">
          <div className="text-center mb-8">
            <h1 className="font-serif text-[28px] font-medium text-ink mb-2">С возвращением</h1>
            <p className="text-sm text-ink-soft">Войдите, чтобы управлять записями</p>
          </div>

          <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-[18px]">
            <Input
              label="Телефон"
              type="tel"
              placeholder="+7 999 000 00 00"
              error={errors.phone?.message}
              {...register('phone', { required: 'Введите телефон' })}
            />
            <Input
              label="Пароль"
              type="password"
              placeholder="••••••••"
              error={errors.password?.message}
              {...register('password', { required: 'Введите пароль' })}
            />

            {error && (
              <div className="bg-danger-bg text-danger text-sm px-4 py-2 rounded-xl">{error}</div>
            )}

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
