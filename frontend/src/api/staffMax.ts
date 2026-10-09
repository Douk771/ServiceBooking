import { api } from '@/api/client'
import type { StaffMaxLinkSessionDto, StaffMaxStatusDto } from '../types'

/** API_CONTRACT_CYCLE25.md §523. Account-level routes (not per shop); 409 is a bare string here. */
export const staffMaxApi = {
  status: () => api.get<StaffMaxStatusDto>('/staff-max').then((r) => r.data),
  createLinkSession: () => api.post<StaffMaxLinkSessionDto>('/staff-max/link-sessions').then((r) => r.data),
  unlink: () => api.delete('/staff-max/link').then(() => undefined),
}
