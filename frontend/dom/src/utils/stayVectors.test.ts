// @vitest-environment node
import { describe, it, expect } from 'vitest'
import vectors from '../../../../contracts/cycle37/stay-vectors.json'
import { quoteMoney, type MoneyInput } from './stayMoney'
import { computeRefund, type RefundInput } from './stayRefund'
import { checkStay, validateSelection, type Occupancy, type StaySettings } from './stayRules'
import { addDays, nightDates } from './stayDates'
import { priceFor, type PricePeriod } from './stayPricing'
import type { HouseCalendarDto } from '../types'

// API_CONTRACT_CYCLE37.md §37.38 — ONE vectors file for the C# and the TS twins (ARCHITECTURE_CYCLE37.md §37.6).

interface MoneyCase {
  id: string
  input: MoneyInput
  expected: { error?: string; extraBeds?: number; lines?: unknown[]; totalRub?: number; prepayRub?: number; dueAtCheckInRub?: number; averageNightRub?: number; firstNightRub?: number }
}

describe('stay-vectors.json — money (StayMoney.Quote)', () => {
  for (const c of vectors.money.cases as unknown as MoneyCase[]) {
    it(`${c.id}`, () => {
      const r = quoteMoney(c.input)
      if (c.expected.error) {
        expect(r.ok).toBe(false)
        if (!r.ok) expect(r.error).toBe(c.expected.error)
        return
      }
      expect(r.ok).toBe(true)
      if (!r.ok) return
      expect(r.extraBeds).toBe(c.expected.extraBeds)
      expect(r.totalRub).toBe(c.expected.totalRub)
      expect(r.prepayRub).toBe(c.expected.prepayRub)
      expect(r.dueAtCheckInRub).toBe(c.expected.dueAtCheckInRub)
      expect(r.averageNightRub).toBe(c.expected.averageNightRub)
      expect(r.firstNightRub).toBe(c.expected.firstNightRub)
      // The vector lines carry only the fields the rule defines; every one of them must match.
      expect(r.lines).toHaveLength(c.expected.lines!.length)
      c.expected.lines!.forEach((exp, i) => expect(r.lines[i]).toMatchObject(exp as object))
    })
  }
})

describe('stay-vectors.json — night price (HousePricing.PriceFor)', () => {
  const houses = vectors.nightPrice.houses as unknown as Record<string, { mode: 'Constant' | 'ByDates'; constantPriceRub: number; periods: PricePeriod[] }>
  for (const c of vectors.nightPrice.cases) {
    it(`${c.id} ${c.house} ${c.date}`, () => {
      const h = houses[c.house]
      expect(priceFor({ mode: h.mode, constantPriceRub: h.constantPriceRub }, h.periods, c.date)).toBe(c.expectedPriceRub)
    })
  }
})

describe('stay-vectors.json — refund (StayRefund.Compute)', () => {
  const common = vectors.refund.common
  for (const c of vectors.refund.cases) {
    it(`${c.id} ${c.policy}/${c.status}/${c.cancelledBy} at ${c.atUtc}`, () => {
      const input: RefundInput = {
        policy: c.policy as RefundInput['policy'],
        status: c.status as RefundInput['status'],
        cancelledBy: c.cancelledBy as RefundInput['cancelledBy'],
        prepayRub: c.prepayRub,
        firstNightRub: c.firstNightRub,
        atUtcMs: Date.parse(c.atUtc),
        checkInDate: common.checkInDate,
        checkInTime: common.checkInTime,
        timeZoneId: common.timeZoneId,
      }
      expect(computeRefund(input)).toEqual(c.expected)
    })
  }
})

interface StayCase {
  id: string
  checkIn: string
  checkOut: string
  settings?: Partial<StaySettings>
  occupancies?: Occupancy[]
  manual?: boolean
  expected: string
}

describe('stay-vectors.json — stay rules (StayRules.CheckStay)', () => {
  const base = vectors.stay.base as { today: string; nowUtc: string; settings: StaySettings; occupancies: Occupancy[] }
  for (const c of vectors.stay.cases as unknown as StayCase[]) {
    it(`${c.id}${(c as { _note?: string })._note ? ` — ${(c as { _note?: string })._note}` : ''}`, () => {
      const result = checkStay({
        checkIn: c.checkIn,
        checkOut: c.checkOut,
        today: base.today,
        nowUtc: base.nowUtc,
        settings: { ...base.settings, ...c.settings },
        occupancies: c.occupancies ?? base.occupancies,
        manual: c.manual,
      })
      expect(result).toBe(c.expected)
    })
  }
})

describe('calendar selection (validateSelection) agrees with the rules on the same vectors', () => {
  const base = vectors.stay.base as { today: string; nowUtc: string; settings: StaySettings; occupancies: Occupancy[] }
  const nowMs = Date.parse(base.nowUtc)

  // Builds the public calendar from raw occupancies the way the server shows it: an unexpired hold is MayFreeUp, other
  // occupancies Occupied, an expired hold is free.
  function calendarFor(settings: StaySettings, occupancies: Occupancy[]): HouseCalendarDto {
    const from = base.today
    const to = addDays(base.today, 600)
    const state = new Map<string, 'Occupied' | 'MayFreeUp'>()
    for (const o of occupancies) {
      const hold = o.holdExpiresAtUtc ? Date.parse(o.holdExpiresAtUtc) : null
      if (hold !== null && hold <= nowMs) continue
      for (const d of nightDates(o.startDate, o.endDate)) state.set(d, hold !== null ? 'MayFreeUp' : 'Occupied')
    }
    const days = nightDates(from, to).map((date) => ({ date, state: state.get(date) ?? ('Free' as const), priceRub: 5000 }))
    return {
      houseId: 'h',
      today: base.today,
      from,
      to,
      minNights: settings.minNights,
      maxNights: settings.maxNights,
      allowGapFill: settings.allowGapFill,
      allowSameDayCheckIn: settings.allowSameDayCheckIn,
      lastNight: addDays(base.today, settings.horizonDays - 1),
      days,
    }
  }

  for (const c of vectors.stay.cases as unknown as StayCase[]) {
    // Manual bookings are staff-only (no public calendar); the rest must give the same verdict.
    if (c.manual) continue
    it(`${c.id}: ${c.expected}`, () => {
      const settings = { ...base.settings, ...c.settings }
      const cal = calendarFor(settings, c.occupancies ?? base.occupancies)
      const verdict = validateSelection(cal, c.checkIn, c.checkOut)
      // Past dates fall outside the generated calendar (it starts today), so the "past" verdict comes from the date test.
      expect(verdict).toBe(c.expected)
    })
  }
})
