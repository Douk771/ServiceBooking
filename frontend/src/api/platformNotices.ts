import { api } from './client'
import type { components } from '../types/api-cycle20.generated'

type Cycle20 = components['schemas']

export type PlatformNoticeDto = Cycle20['PlatformNoticeDto']
export type PlatformNoticeListDto = Cycle20['PlatformNoticeListDto']
export type PlatformNoticeKind = Cycle20['PlatformNoticeKind']

/**
 * API_CONTRACT_CYCLE20.md §434.1–§434.3 (US-20-03, Т20-02) — the addressee-facing side of platform
 * notices. `/api/legal/notices*` sits under the `/api/legal/` prefix, which is ALREADY in the 451
 * allow-list (§430) — no client-side bypass is needed for a suspended/gated account to keep reading
 * these (ARCHITECTURE_CYCLE20.md §404.5): this module doesn't special-case 451 at all.
 */
export const platformNoticesApi = {
  /** `scope: 'pending'` (default) drives the banner — unread, non-revoked, still-visible notices for
   *  the caller. `scope: 'all'` backs `/notices` and the "Ваша подписка" block (read + revoked too). */
  getNotices: (scope: 'pending' | 'all' = 'pending') =>
    api.get<PlatformNoticeListDto>('/legal/notices', { params: { scope } }).then((r) => r.data),

  /** Idempotent: a repeat click answers 200 without moving `acknowledgedAt` or writing a new row. */
  acknowledge: (id: string) => api.post<PlatformNoticeDto>(`/legal/notices/${id}/acknowledge`).then((r) => r.data),

  /** §434.3 — the future-edition snapshot. Fetched as text (still through `api`, so the auth header is
   *  attached) and rendered ONLY inside `<iframe sandbox="" srcdoc>` by the caller — never
   *  `dangerouslySetInnerHTML` into the app's own DOM (Р11, XSS via attachment). */
  getAttachment: (id: string) =>
    api.get<string>(`/legal/notices/${id}/attachment`, { responseType: 'text' }).then((r) => r.data),
}
