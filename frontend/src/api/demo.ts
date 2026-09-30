import { AxiosError } from 'axios'
import { api } from './client'
import type { AuthResponse } from '../types'
import type { components } from '../types/api-cycle35.generated'

export type DemoRole = components['schemas']['DemoRole']
export type DemoRoleDto = components['schemas']['DemoRoleDto']
export type DemoStatusDto = components['schemas']['DemoStatusDto']
/** Which demo stand this front end is: ezbook «Запись» (`services`, demo.visit) or goods «Заказы» (`orders`, demo.zakaz). */
export type DemoProduct = components['schemas']['DemoProduct']

export const demoApi = {
  /** `GET /api/demo/status` (API_CONTRACT_CYCLE35.md §35.21). Anonymous. Outside demo mode the route answers 404 with an
   *  empty body — that is "not a demo", not an error, so it resolves to `null` (no banner, no role buttons). Any
   *  other failure (network, 5xx) still rejects. `services` (ezbook) sends no parameter — the request is the same as in
   *  cycle 28; `orders` (goods) asks for `?product=orders` and gets the three shop roles. */
  getStatus: (product: DemoProduct = 'services'): Promise<DemoStatusDto | null> =>
    (product === 'orders'
      ? api.get<DemoStatusDto>('/demo/status', { params: { product } })
      : api.get<DemoStatusDto>('/demo/status')
    )
      .then((r) => r.data)
      .catch((err: unknown) => {
        if (err instanceof AxiosError && err.response?.status === 404) return null
        throw err
      }),

  /** `POST /api/demo/login` (§35.22) — a passwordless sign-in under one of the six fixed demo roles (three per
   *  product). Same response shape as `POST /api/auth/login`. */
  login: (role: DemoRole) => api.post<AuthResponse>('/demo/login', { role }).then((r) => r.data),
}
