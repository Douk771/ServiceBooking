// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { can, cabinetTabs, defaultCabinetTab, roleLabel } from './permissions'
import type { StaysPermission } from '../types'

// ARCHITECTURE_CYCLE37.md §37.9 — the rights table the server sends as `myPermissions`.
const OWNER: StaysPermission[] = ['ManageCompany', 'ManageHouses', 'EditHouseContent', 'ViewBookings', 'ManageBookings', 'ManageBlocks', 'ViewSchedule', 'ViewCabinet']
const MANAGER: StaysPermission[] = ['EditHouseContent', 'ViewBookings', 'ManageBookings', 'ManageBlocks', 'ViewSchedule', 'ViewCabinet']
const HOUSEKEEPER: StaysPermission[] = ['ViewSchedule']

const ids = (p: StaysPermission[]) => cabinetTabs(p).map((t) => t.id)

describe('cabinet tabs by rights', () => {
  it('owner sees everything, in menu order', () => {
    expect(ids(OWNER)).toEqual(['board', 'bookings', 'houses', 'schedule', 'settings', 'staff', 'notifications', 'link'])
  })

  it('manager has no settings, staff or notifications', () => {
    expect(ids(MANAGER)).toEqual(['board', 'bookings', 'houses', 'schedule', 'link'])
  })

  it('housekeeper sees only the schedule', () => {
    expect(ids(HOUSEKEEPER)).toEqual(['schedule'])
  })

  it('a member with no rights has no tabs', () => {
    expect(cabinetTabs([])).toEqual([])
    expect(defaultCabinetTab([])).toBeNull()
  })

  it('every tab has a route segment the app declares', () => {
    expect(cabinetTabs(OWNER).map((t) => t.to)).toEqual(['board', 'bookings', 'houses', 'schedule', 'settings', 'staff', 'notifications', 'link'])
  })
})

describe('default screen', () => {
  it('board for owner and manager, schedule for the housekeeper', () => {
    expect(defaultCabinetTab(OWNER)).toBe('board')
    expect(defaultCabinetTab(MANAGER)).toBe('board')
    expect(defaultCabinetTab(HOUSEKEEPER)).toBe('schedule')
  })
})

describe('can / roleLabel', () => {
  it('checks one right and tolerates a missing list', () => {
    expect(can(OWNER, 'ManageCompany')).toBe(true)
    expect(can(MANAGER, 'ManageCompany')).toBe(false)
    expect(can(undefined, 'ViewCabinet')).toBe(false)
  })
  it('role names for the header', () => {
    expect([roleLabel('Owner'), roleLabel('Manager'), roleLabel('Housekeeper')]).toEqual(['Владелец', 'Управляющий', 'Горничная'])
  })
})
