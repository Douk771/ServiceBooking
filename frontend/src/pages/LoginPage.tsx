import { useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router-dom'
import { authApi } from '../api/auth'
import { useAuthStore } from '../store/authStore'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { Card } from '../components/ui/Card'
import { useState } from 'react'

interface FormData {
  email: string
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
      const res = await authApi.login(data.email, data.password)
      setAuth({ id: res.userId, email: res.email, firstName: res.firstName, lastName: res.lastName, roles: res.roles }, res.token)
      navigate('/')
    } catch {
      setError('Неверный email или пароль')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-b from-orange-50 to-white flex items-center justify-center px-4">
      <Card className="w-full max-w-md p-8">
        <div className="text-center mb-8">
          <div className="text-4xl mb-3">📅</div>
          <h1 className="text-2xl font-bold text-gray-900">Добро пожаловать</h1>
          <p className="text-gray-500 mt-1">Войдите в свой аккаунт</p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
          <Input
            label="Email"
            type="email"
            placeholder="your@email.com"
            error={errors.email?.message}
            {...register('email', { required: 'Введите email' })}
          />
          <Input
            label="Пароль"
            type="password"
            placeholder="••••••••"
            error={errors.password?.message}
            {...register('password', { required: 'Введите пароль' })}
          />

          {error && (
            <div className="bg-red-50 text-red-600 text-sm px-4 py-2 rounded-xl">{error}</div>
          )}

          <Button type="submit" size="lg" loading={loading} className="mt-2">
            Войти
          </Button>
        </form>

        <p className="text-center text-sm text-gray-500 mt-6">
          Нет аккаунта?{' '}
          <Link to="/register" className="text-primary-600 font-medium hover:underline">
            Зарегистрироваться
          </Link>
        </p>
      </Card>
    </div>
  )
}
