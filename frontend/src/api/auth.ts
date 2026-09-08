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
    /** Required by API_CONTRACT.md §5 (BREAKING № 1) — a missing or `false` value is a 400. */
    acceptedLegal: boolean
  }) => api.post<AuthResponse>('/auth/register', data).then((r) => r.data),
}
