import { api } from './client'
import type { AuthResponse } from '../types'

export const authApi = {
  login: (phone: string, password: string) =>
    api.post<AuthResponse>('/auth/login', { phone, password }).then((r) => r.data),

  register: (data: {
    firstName: string
    lastName: string
    phone: string
    password: string
    email?: string
  }) => api.post<AuthResponse>('/auth/register', data).then((r) => r.data),
}
