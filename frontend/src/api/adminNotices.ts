import { api } from './client'
import type { components } from '../types/api-cycle20.generated'

type Cycle20 = components['schemas']

export type AdminPlatformNoticeDto = Cycle20['AdminPlatformNoticeDto']
export type PagedAdminPlatformNotices = Cycle20['PagedAdminPlatformNotices']
export type PlatformNoticeCreateInput = Cycle20['PlatformNoticeCreateInput']
export type PlatformNoticePreviewDto = Cycle20['PlatformNoticePreviewDto']
export type PlatformNoticeKind = Cycle20['PlatformNoticeKind']
export type PlatformNoticeCreatableKind = Cycle20['PlatformNoticeCreatableKind']
export type NoticeAudienceType = Cycle20['NoticeAudienceType']
export type TermsChangeDocumentType = Cycle20['TermsChangeDocumentType']

/**
 * API_CONTRACT_CYCLE20.md §434.4–§434.8 (US-20-03) — SuperAdmin side: list with addressee/read
 * counters, publish (matrix §434.5), dry-run preview (same validator, writes nothing), revoke, and the
 * attachment for SuperAdmin's own preview of what was published. There is NO `PUT` — a published
 * notice is never edited (§443), only ever revoked.
 */
export const adminNoticesApi = {
  list: (params: { kind?: PlatformNoticeKind; page?: number; pageSize?: number }) =>
    api.get<PagedAdminPlatformNotices>('/admin/notices', { params }).then((r) => r.data),

  publish: (input: PlatformNoticeCreateInput) =>
    api.post<AdminPlatformNoticeDto>('/admin/notices', input).then((r) => r.data),

  preview: (input: PlatformNoticeCreateInput) =>
    api.post<PlatformNoticePreviewDto>('/admin/notices/preview', input).then((r) => r.data),

  revoke: (id: string, reason: string) =>
    api.post<AdminPlatformNoticeDto>(`/admin/notices/${id}/revoke`, { reason }).then((r) => r.data),

  getAttachment: (id: string) =>
    api.get<string>(`/admin/notices/${id}/attachment`, { responseType: 'text' }).then((r) => r.data),
}
