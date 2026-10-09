import { describe, it, expect } from 'vitest'
import { businessTodayIn, cabinetTabs, defaultCabinetTab } from './cabinetTabs'
import type { StaysPermission } from './types'

const OWNER: StaysPermission[] = ['ManageCompany', 'ViewBookings', 'ManageBookings', 'ManageServices', 'EditServiceContent', 'ManageServiceDates', 'ViewSchedule', 'ViewCabinet']
const BATHER: StaysPermission[] = ['ViewSchedule']

describe('cabinetTabs', () => {
  it('gives the owner the day first, then bookings and resources', () => {
    const ids = cabinetTabs(OWNER).map((t) => t.id)
    expect(ids.slice(0, 3)).toEqual(['day', 'orders', 'resources'])
    expect(ids).toContain('settings')
  })
  it('gives a bather «Расписание» and nothing else', () => {
    expect(cabinetTabs(BATHER).map((t) => t.id)).toEqual(['schedule'])
  })
  it('opens the day for whoever sees bookings and the schedule for a bather', () => {
    expect(defaultCabinetTab(OWNER)).toBe('day')
    expect(defaultCabinetTab(BATHER)).toBe('schedule')
    expect(defaultCabinetTab([])).toBeNull()
  })
})

describe('businessTodayIn', () => {
  it('counts the hours before 06:00 local time to the previous business day', () => {
    // 2026-01-16 01:00 in Novosibirsk (UTC+7) = 2026-01-15 18:00Z
    expect(businessTodayIn('Asia/Novosibirsk', new Date('2026-01-15T18:00:00Z'))).toBe('2026-01-15')
    // 07:00 local the same day
    expect(businessTodayIn('Asia/Novosibirsk', new Date('2026-01-16T00:00:00Z'))).toBe('2026-01-16')
  })
  it('survives an unknown zone', () => {
    expect(businessTodayIn('Nowhere/None', new Date('2026-01-16T12:00:00Z'))).toBe('2026-01-16')
  })
})
