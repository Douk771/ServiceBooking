import type { StaysMyRole, StaysPermission } from './types'

export interface CabinetTab {
  id: 'day' | 'orders' | 'resources' | 'schedule' | 'settings' | 'staff' | 'notifications' | 'link'
  label: string
  /** Relative to `/cabinet/:companyId/`; the day tab needs a date, see `dayTabPath`. */
  to: string
}

const has = (perms: readonly StaysPermission[], ...any: StaysPermission[]) => any.some((p) => perms.includes(p))

/** The tabs of a «Бани» cabinet for the rights the server gave (the menu never reads the role name). A bather sees «Расписание» only. */
export function cabinetTabs(perms: readonly StaysPermission[]): CabinetTab[] {
  const tabs: CabinetTab[] = []
  if (has(perms, 'ViewBookings')) tabs.push({ id: 'day', label: 'День', to: 'service-day' }, { id: 'orders', label: 'Брони', to: 'orders' })
  if (has(perms, 'ManageServices', 'EditServiceContent', 'ManageServiceDates')) tabs.push({ id: 'resources', label: 'Ресурсы', to: 'resources' })
  if (has(perms, 'ViewSchedule')) tabs.push({ id: 'schedule', label: 'Расписание', to: 'schedule' })
  if (has(perms, 'ManageCompany')) {
    tabs.push({ id: 'settings', label: 'Настройки', to: 'settings' }, { id: 'staff', label: 'Персонал', to: 'staff' }, { id: 'notifications', label: 'Уведомления', to: 'notifications' })
  }
  if (has(perms, 'ViewCabinet')) tabs.push({ id: 'link', label: 'Ссылка и QR', to: 'link' })
  return tabs
}

/** Where `/cabinet/:companyId` goes: «День услуг» for whoever sees bookings, «Расписание» for a bather. */
export function defaultCabinetTab(perms: readonly StaysPermission[]): CabinetTab['id'] | null {
  if (has(perms, 'ViewBookings')) return 'day'
  if (has(perms, 'ViewSchedule')) return 'schedule'
  return cabinetTabs(perms)[0]?.id ?? null
}

const ROLE_LABELS: Record<StaysMyRole, string> = {
  Owner: 'Владелец',
  Manager: 'Администратор',
  Housekeeper: 'Банщик',
  SuperAdmin: 'Администратор платформы',
}
export const roleLabel = (role: StaysMyRole): string => ROLE_LABELS[role]

const pad = (n: number) => String(n).padStart(2, '0')

/**
 * The business day «now» is in (it runs 06:00 → 06:00 in the company's time zone), as `yyyy-MM-dd`. The server's own `today` wins
 * wherever a screen has one; this is only for the first redirect into «День услуг».
 */
export function businessTodayIn(timeZoneId: string, now: Date = new Date()): string {
  const shifted = new Date(now.getTime() - 6 * 3_600_000)
  try {
    const p = new Intl.DateTimeFormat('en-CA', { timeZone: timeZoneId, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(shifted)
    const get = (t: string) => p.find((x) => x.type === t)?.value ?? ''
    return `${get('year')}-${get('month')}-${get('day')}`
  } catch {
    return `${shifted.getUTCFullYear()}-${pad(shifted.getUTCMonth() + 1)}-${pad(shifted.getUTCDate())}`
  }
}
