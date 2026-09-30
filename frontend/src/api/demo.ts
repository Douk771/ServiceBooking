import { AxiosError } from 'axios'
import { api } from './client'
import type { AuthResponse } from '../types'
import type { components } from '../types/api-cycle28.generated'

export type DemoRole = components['schemas']['DemoRole']
export type DemoRoleDto = components['schemas']['DemoRoleDto']
export type DemoStatusDto = components['schemas']['DemoStatusDto']

export const demoApi = {
  /** `GET /api/demo/status` (API_CONTRACT_CYCLE28.md §597). Anonymous. Outside demo mode the route answers 404 with an
   *  empty body — that is "not a demo", not an error, so it resolves to `null` (no banner, no role buttons). Any
   *  other failure (network, 5xx) still rejects. */
  getStatus: (): Promise<DemoStatusDto | null> =>
    api
      .get<DemoStatusDto>('/demo/status')
      .then((r) => r.data)
      .catch((err: unknown) => {
        if (err instanceof AxiosError && err.response?.status === 404) return null
        throw err
      }),

  /** `POST /api/demo/login` (§598) — a passwordless sign-in under one of three fixed demo roles. Same response shape
   *  as `POST /api/auth/login`. */
  login: (role: DemoRole) => api.post<AuthResponse>('/demo/login', { role }).then((r) => r.data),
}
