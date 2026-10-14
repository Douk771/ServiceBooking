import type { StaysMyRole, StaysPermission } from '../types'

export { can } from '@/utils/slots/slotPermissions'

export interface CabinetTab {
  id: 'board' | 'bookings' | 'houses' | 'services' | 'schedule' | 'settings' | 'staff' | 'notifications' | 'link'
  label: string
  /** Path segment after `/cabinet/:companyId/`. */
  to: string
}

const has = (perms: readonly StaysPermission[], ...any: StaysPermission[]) => any.some((p) => perms.includes(p))

/** Cabinet tabs of a company, in menu order, for the rights the caller has. */
export function cabinetTabs(perms: readonly StaysPermission[]): CabinetTab[] {
  const tabs: CabinetTab[] = []
  if (has(perms, 'ViewBookings')) {
    tabs.push({ id: 'board', label: 'Шахматка', to: 'board' }, { id: 'bookings', label: 'Брони', to: 'bookings' })
  }
  if (has(perms, 'ViewCabinet', 'EditHouseContent', 'ManageHouses')) tabs.push({ id: 'houses', label: 'Дома', to: 'houses' })
  if (has(perms, 'ManageServices', 'EditServiceContent', 'ManageServiceDates')) tabs.push({ id: 'services', label: 'Услуги', to: 'services' })
  if (has(perms, 'ViewSchedule')) tabs.push({ id: 'schedule', label: 'График', to: 'schedule' })
  if (has(perms, 'ManageCompany')) {
    tabs.push(
      { id: 'settings', label: 'Настройки', to: 'settings' },
      { id: 'staff', label: 'Персонал', to: 'staff' },
      { id: 'notifications', label: 'Уведомления', to: 'notifications' },
    )
  }
  if (has(perms, 'ViewCabinet')) tabs.push({ id: 'link', label: 'Ссылка и QR', to: 'link' })
  return tabs
}

/** Where `/cabinet/:companyId` goes: the board for owner and manager, the schedule for a housekeeper (§37.14.5). */
export function defaultCabinetTab(perms: readonly StaysPermission[]): CabinetTab['to'] | null {
  if (has(perms, 'ViewBookings')) return 'board'
  if (has(perms, 'ViewSchedule')) return 'schedule'
  return cabinetTabs(perms)[0]?.to ?? null
}

const ROLE_LABELS: Record<StaysMyRole, string> = {
  Owner: 'Владелец',
  Manager: 'Управляющий',
  Housekeeper: 'Горничная',
  SuperAdmin: 'Администратор платформы',
}

export const roleLabel = (role: StaysMyRole): string => ROLE_LABELS[role]

export const POSITION_LABELS = { Manager: 'Управляющий', Housekeeper: 'Горничная' } as const
