import { api } from '@/api/client'
import type { StaffPosition } from '../types'

/**
 * Staff of a «Дома» company (API_CONTRACT_CYCLE37.md §37.21.3). The member list itself is the shared
 * `GET /api/companies/{id}/members`; only the fields the screen reads are typed here.
 */
export interface StaysMemberDto {
  id: string
  userId: string
  firstName: string
  lastName: string
  phone: string
  role: string
  /** Null for the owner (§37.21.3). */
  position?: StaffPosition | null
}

export const staysMembersApi = {
  list: (companyId: string) => api.get<StaysMemberDto[]>(`/companies/${companyId}/members`).then((r) => r.data),
  /** Role is always `Master` in a «Дома» company and `position` is mandatory; an unknown phone creates the account (names needed). */
  add: (companyId: string, input: { phone: string; firstName: string; lastName: string; position: StaffPosition }) =>
    api.post<StaysMemberDto>(`/companies/${companyId}/members`, { ...input, role: 'Master' }).then((r) => r.data),
  setPosition: (companyId: string, memberId: string, position: StaffPosition) =>
    api.put(`/companies/${companyId}/members/${memberId}/position`, { position }).then(() => undefined),
  remove: (companyId: string, memberId: string) => api.delete(`/companies/${companyId}/members/${memberId}`).then(() => undefined),
}
