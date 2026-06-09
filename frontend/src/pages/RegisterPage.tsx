import { useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router-dom'
import { authApi } from '../api/auth'
import { useAuthStore } from '../store/authStore'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { Card } from '../components/ui/Card'
import { useState } from 'react'

interface FormData {
  firstName: string
  lastName: string
  email: string
  password: string
  phone?: string
}

export function RegisterPage() {
  const { register, handleSubmit, formState: { errors } } = useForm<FormData>()
  const { setAuth } = useAuthStore()
  const navigate = useNavigate()
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const onSubmit = async (data: FormData) => {
    setLoading(true)
    setError('')
    try {
      const res = await authApi.register(data)
      setAuth({ id: res.userId, email: res.email, firstName: res.firstName, lastName: res.lastName, roles: res.roles }, res.token)
      navigate('/')
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: string } })?.response?.data
      setError(typeof msg === 'string' ? msg : 'Ошибка регистрации. Попробуйте снова.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-b from-orange-50 to-white flex items-center justify-center px-4">
      <Card className="w-full max-w-md p-8">
        <div className="text-center mb-8">
          <div className="text-4xl mb-3">🎉</div>
          <h1 className="text-2xl font-bold text-gray-900">Создать аккаунт</h1>
          <p className="text-gray-500 mt-1">Присоединитесь — это бесплатно</p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
          <div className="grid grid-cols-2 gap-3">
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
          <Input
            label="Email"
            type="email"
            placeholder="your@email.com"
            error={errors.email?.message}
            {...register('email', { required: 'Введите email' })}
          />
          <Input
            label="Телефон (необязательно)"
            type="tel"
            placeholder="+7 999 000 00 00"
            {...register('phone')}
          />
          <Input
            label="Пароль"
            type="password"
            placeholder="Минимум 8 символов"
            error={errors.password?.message}
            {...register('password', { required: 'Введите пароль', minLength: { value: 8, message: 'Минимум 8 символов' } })}
          />

          {error && (
            <div className="bg-red-50 text-red-600 text-sm px-4 py-2 rounded-xl">{error}</div>
          )}

          <Button type="submit" size="lg" loading={loading} className="mt-2">
            Зарегистрироваться
          </Button>
        </form>

        <p className="text-center text-sm text-gray-500 mt-6">
          Уже есть аккаунт?{' '}
          <Link to="/login" className="text-primary-600 font-medium hover:underline">
            Войти
          </Link>
        </p>
      </Card>
    </div>
  )
}
