import { api } from '@/api/client'
import type {
  AcceptanceInput,
  PickupSettingsInput,
  PickupSlotsDto,
  ShopAcceptanceDto,
  ShopManageDto,
  ShopOrderingStatusDto,
  SpecialDayDto,
  SpecialDayInput,
  WorkingHoursDto,
  WorkingHoursInput,
} from '../types'

/** API_CONTRACT_CYCLE24.md §473–§476. Working hours / pickup settings / special days are owner-only writes; acceptance is staff. */
export const scheduleApi = {
  workingHours: (shopId: string) => api.get<WorkingHoursDto>(`/shops/${shopId}/working-hours`).then((r) => r.data),
  putWorkingHours: (shopId: string, data: WorkingHoursInput) =>
    api.put<WorkingHoursDto>(`/shops/${shopId}/working-hours`, data).then((r) => r.data),

  specialDays: (shopId: string) => api.get<SpecialDayDto[]>(`/shops/${shopId}/special-days`).then((r) => r.data),
  putSpecialDay: (shopId: string, date: string, data: SpecialDayInput) =>
    api.put<SpecialDayDto>(`/shops/${shopId}/special-days/${date}`, data).then((r) => r.data),
  deleteSpecialDay: (shopId: string, date: string) => api.delete<void>(`/shops/${shopId}/special-days/${date}`),

  putPickupSettings: (shopId: string, data: PickupSettingsInput) =>
    api.put<ShopManageDto>(`/shops/${shopId}/pickup-settings`, data).then((r) => r.data),

  putAcceptance: (shopId: string, data: AcceptanceInput) =>
    api.put<ShopAcceptanceDto>(`/shops/${shopId}/acceptance`, data).then((r) => r.data),
  orderingStatus: (shopId: string) =>
    api.get<ShopOrderingStatusDto>(`/shops/${shopId}/ordering-status`).then((r) => r.data),

  /** Staff variant: no preparation time / pause / `scheduledEnabled` — used by «Изменить время» (§477.2, §481). */
  staffPickupSlots: (shopId: string, date: string) =>
    api.get<PickupSlotsDto>(`/shops/${shopId}/pickup-slots`, { params: { date } }).then((r) => r.data),
}
