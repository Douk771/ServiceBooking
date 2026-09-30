import { describe, it, expect } from 'vitest'
import { planProfileZone, type ZoneBaseline } from './profileZone'
import type { City } from '../../types'

const city = (id: number, offset: number, label = 'Город'): City => ({
  id, name: label, region: '', timeZoneId: 'X', utcOffsetMinutes: offset, label,
})
const base = (over: Partial<ZoneBaseline> = {}): ZoneBaseline => ({
  cityId: 1, zoneId: 'Asia/Barnaul', offsetMinutes: 420, isManual: false, ...over,
})
const offsets: Record<string, number> = { 'Asia/Omsk': 360, 'Asia/Barnaul': 420 }
const offsetOf = (z: string) => offsets[z] ?? null
const barnaul = city(1, 420, 'Барнаул, Алтайский край')

describe('planProfileZone — shop (manualAllowed = false)', () => {
  const plan = (c: City | null, b = base()) => planProfileZone(b, { city: c, manual: false, manualZoneId: '' }, false, offsetOf)

  it('same city -> nothing', () => {
    expect(plan(barnaul)).toEqual({ confirm: null })
  })
  it('other city with the same offset -> cityId, no confirm', () => {
    expect(plan(city(2, 420))).toEqual({ cityId: 2, confirm: null })
  })
  it('other offset -> confirm', () => {
    expect(plan(city(3, 180))).toEqual({ cityId: 3, confirm: { from: 'UTC+7', to: 'UTC+3' } })
  })
  it('unknown base offset -> confirm (V29-12)', () => {
    expect(plan(city(3, 180), base({ offsetMinutes: null }))).toEqual({ cityId: 3, confirm: { from: 'Asia/Barnaul', to: 'UTC+3' } })
  })
  it('never emits timeZoneId', () => {
    const p = planProfileZone(base({ isManual: true }), { city: barnaul, manual: true, manualZoneId: 'Asia/Omsk' }, false, offsetOf)
    expect('timeZoneId' in p).toBe(false)
  })
})

describe('planProfileZone — salon', () => {
  const plan = (b: ZoneBaseline, d: { city: City | null; manual: boolean; manualZoneId: string }) =>
    planProfileZone(b, d, true, offsetOf)

  it('manual turned on with another offset', () => {
    expect(plan(base(), { city: barnaul, manual: true, manualZoneId: 'Asia/Omsk' })).toEqual({
      timeZoneId: 'Asia/Omsk', confirm: { from: 'UTC+7', to: 'UTC+6' },
    })
  })
  it('same manual zone -> no key', () => {
    const b = base({ isManual: true, zoneId: 'Asia/Omsk', offsetMinutes: 360 })
    expect(plan(b, { city: barnaul, manual: true, manualZoneId: 'Asia/Omsk' })).toEqual({ confirm: null })
  })
  it('manual turned off, same city -> null and confirm without number', () => {
    const b = base({ isManual: true, zoneId: 'Asia/Omsk', offsetMinutes: 360 })
    expect(plan(b, { city: barnaul, manual: false, manualZoneId: '' })).toEqual({
      timeZoneId: null, confirm: { from: 'UTC+6', to: 'пояс города Барнаул, Алтайский край' },
    })
  })
  it('manual turned off + new city -> null, cityId and the new city offset', () => {
    const b = base({ isManual: true, zoneId: 'Asia/Omsk', offsetMinutes: 360 })
    expect(plan(b, { city: city(5, 180), manual: false, manualZoneId: '' })).toEqual({
      cityId: 5, timeZoneId: null, confirm: { from: 'UTC+6', to: 'UTC+3' },
    })
  })
  it('city changed while manual stays -> cityId only', () => {
    const b = base({ isManual: true, zoneId: 'Asia/Omsk', offsetMinutes: 360 })
    expect(plan(b, { city: city(5, 180), manual: true, manualZoneId: 'Asia/Omsk' })).toEqual({ cityId: 5, confirm: null })
  })
  it('empty manual field while manual was on -> null', () => {
    const b = base({ isManual: true, zoneId: 'Asia/Omsk', offsetMinutes: 360 })
    expect(plan(b, { city: barnaul, manual: true, manualZoneId: '  ' }).timeZoneId).toBeNull()
  })
  it('browser does not know the zone -> key sent, no confirm', () => {
    expect(plan(base(), { city: barnaul, manual: true, manualZoneId: 'Asia/Nowhere' })).toEqual({
      timeZoneId: 'Asia/Nowhere', confirm: null,
    })
  })
})
