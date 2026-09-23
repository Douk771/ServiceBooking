import { api } from './client'
import type { Company } from '../types'

export interface MasterPublicDto {
  userId: string
  firstName: string
  lastName: string
  avatarUrl?: string
  bio?: string
  /**
   * API_CONTRACT_CYCLE10.md §124 — always `true` for an anonymous/client caller (otherwise the
   * master wouldn't be in the list at all). Only becomes `false` when `includeHidden: true` was
   * honored (staff of this company / SuperAdmin) — the UI uses it to label why clients don't see
   * this master.
   */
  providesServices: boolean
}

export interface MemberDto {
  id: string
  userId: string
  firstName: string
  lastName: string
  phone: string
  email?: string | null
  avatarUrl?: string
  role: string
  bio?: string
  serviceIds: string[]
  commissionPercent: number
  /**
   * US-62 (API_CONTRACT_CYCLE6.md §40.2): whether this member is offered to clients as a specialist.
   * Scoped to this person's membership in THIS company, not their account — the same person can have
   * a different value in another company. Defaults to `true` for every existing member post-migration.
   */
  providesServices: boolean
}

export interface CreateCompanyPayload {
  name: string
  slug: string
  description?: string
  address?: string
  phone?: string
  email?: string
  allowSelfBooking: boolean
  showInPublicListing?: boolean
  /** API_CONTRACT_CYCLE4.md §31.2 — required; the one breaking change of cycle 4. */
  cityId: number
  timeZoneId?: string | null
  /** API_CONTRACT_CYCLE5.md §42.1 (BREAKING № 3) — acceptance of `TermsOwner`, required. */
  ownerTerms: { version: string }
}

/** §42.1 — the response now carries a fresh token (claim `lco`) alongside the company. Without
 *  saving it immediately, the owner would get owner-gate 451 on their own just-created company until
 *  their next login. */
export interface CreateCompanyResponse {
  company: Company
  token: string
}

export interface UpdateCompanyPayload {
  name?: string
  description?: string
  address?: string
  phone?: string
  email?: string
  allowSelfBooking?: boolean
  requirePrepayment?: boolean
  showInPublicListing?: boolean
  /** API_CONTRACT_CYCLE4.md §31.3 — `timeZoneId: null` explicitly resets to the city-derived zone. */
  cityId?: number
  timeZoneId?: string | null
  /**
   * US-65/Q5 (API_CONTRACT_CYCLE6.md §41.4): how many days ahead a client may book online.
   * Omitted/undefined = leave unchanged; `0` = reset to the server default (90 days), never "closed".
   */
  bookingHorizonDays?: number
}

export interface CompanyPhotoUsage {
  companyId: string
  usedBytes: number
  photoCount: number
  quotaMb: number | null
  percentUsed: number | null
  /** API_CONTRACT_CYCLE5.md §49.5, §59.1 — `Forever` was removed from the model (152-ФЗ ч. 7 ст. 5
   *  prohibits indefinite retention); the migration converted existing `Forever` plans to 12 months. */
  retention: 'SixMonths' | 'TwelveMonths'
}

export const companiesApi = {
  getAll: () => api.get<Company[]>('/companies').then((r) => r.data),
  getBySlug: (slug: string) => api.get<Company>(`/companies/${slug}`).then((r) => r.data),
  getMy: () => api.get<Company[]>('/companies/my').then((r) => r.data),
  getMemberOf: () => api.get<Company[]>('/companies/member').then((r) => r.data),
  create: (data: CreateCompanyPayload) => api.post<CreateCompanyResponse>('/companies', data).then((r) => r.data),
  update: (id: string, data: UpdateCompanyPayload) => api.put<Company>(`/companies/${id}`, data).then((r) => r.data),
  getMasters: (id: string, serviceId?: string, includeHidden = false) =>
    api
      .get<MasterPublicDto[]>(`/companies/${id}/masters`, {
        params: { ...(serviceId ? { serviceId } : {}), ...(includeHidden ? { includeHidden: 'true' } : {}) },
      })
      .then((r) => r.data),
  getMembers: (id: string) => api.get<MemberDto[]>(`/companies/${id}/members`).then((r) => r.data),
  addMember: (
    id: string,
    phone: string,
    firstName: string,
    lastName: string,
    role: string,
    bio?: string,
    email?: string,
  ) =>
    api
      .post<MemberDto>(`/companies/${id}/members`, { phone, firstName, lastName, role, bio, email })
      .then((r) => r.data),
  removeMember: (companyId: string, memberId: string) => api.delete(`/companies/${companyId}/members/${memberId}`),
  updateMemberServices: (companyId: string, memberId: string, serviceIds: string[]) =>
    api.put(`/companies/${companyId}/members/${memberId}/services`, serviceIds),
  updateMemberCommission: (companyId: string, memberId: string, commissionPercent: number) =>
    api.put(`/companies/${companyId}/members/${memberId}/commission`, { commissionPercent }),
  /**
   * US-62 (API_CONTRACT_CYCLE6.md §40.3). Turning the flag OFF for someone with future bookings
   * returns 409 unless `confirm` is `true` — callers retry with `confirm: true` after the operator
   * accepts the warning. Turning it ON never needs confirmation.
   */
  updateMemberProvidesServices: (companyId: string, memberId: string, providesServices: boolean, confirm = false) =>
    api.put(`/companies/${companyId}/members/${memberId}/provides-services`, { providesServices, confirm }),
  uploadLogo: (id: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<Company>(`/companies/${id}/logo`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      .then((r) => r.data)
  },
  getPhotoUsage: (id: string) => api.get<CompanyPhotoUsage>(`/companies/${id}/photo-usage`).then((r) => r.data),
}
