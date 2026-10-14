import { api } from '@/api/client'
import type { BathsStaffPosition } from '../cabinet/types'

/**
 * Staff of a «Бани» company (API_CONTRACT_CYCLE42.md §42.21). The list is the shared `GET /api/companies/{id}/members`; the role is always
 * `Master` and the position is `Manager` («Администратор») or `Housekeeper` («Банщик»). Only the fields the screen reads are typed.
 */
export interface BathsMemberDto {
  id: string
  userId: string
  firstName: string
  lastName: string
  phone: string
  role: string
  /** Null for the owner. */
  position?: BathsStaffPosition | null
}

export const bathsMembersApi = {
  list: (companyId: string) => api.get<BathsMemberDto[]>(`/companies/${companyId}/members`).then((r) => r.data),
  add: (companyId: string, input: { phone: string; firstName: string; lastName: string; position: BathsStaffPosition }) =>
    api.post<BathsMemberDto>(`/companies/${companyId}/members`, { ...input, role: 'Master' }).then((r) => r.data),
  setPosition: (companyId: string, memberId: string, position: BathsStaffPosition) =>
    api.put(`/companies/${companyId}/members/${memberId}/position`, { position }).then(() => undefined),
  remove: (companyId: string, memberId: string) => api.delete(`/companies/${companyId}/members/${memberId}`).then(() => undefined),
}
