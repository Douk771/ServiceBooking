// @vitest-environment node
import { describe, it, expect } from 'vitest'
import vectors from '../../../../contracts/cycle39/service-vectors.json'
import { businessDateOf, businessToUtc } from './businessClock'
import { businessDateLabel, guestTimeLabel, priceRuleLabel, staffTimeLabel, startLabel } from './serviceTimeFormat'
import { hourPrices, quoteServiceMoney, type MoneyItem, type PriceRuleLike } from './serviceMoney'
import { validatePriceRule, validateWindows, type PriceRuleDraft, type WindowLike } from './serviceWindows'

// API_CONTRACT_CYCLE39.md §39.38 — ONE vectors file for the C# and the TS twins (ARCHITECTURE_CYCLE39.md §39.3, §39.6).
// Sections `starts`, `overlap`, `refund` and `reminderTemplate` are server-only (the calculator and the texts live on the server).

describe('service-vectors.json — businessDay (BusinessClock)', () => {
  for (const c of vectors.businessDay.cases) {
    it(c.id, () => expect(businessDateOf(c.utc)).toEqual(c.expected))
  }
  for (const c of vectors.businessDay.toUtc) {
    it(c.id, () => expect(businessToUtc(c.businessDate, c.minute)).toBe(c.expectedUtc))
  }
  it('round-trips every vector instant', () => {
    for (const c of vectors.businessDay.cases) {
      expect(businessToUtc(c.expected.businessDate, c.expected.minute)).toBe(c.utc)
    }
  })
})

describe('service-vectors.json — windows (validateWindows)', () => {
  for (const c of vectors.windows.cases) {
    it(c.id, () => {
      const r = validateWindows(c.windows as WindowLike[])
      expect(r.ok).toBe(c.expected.ok)
      if (r.ok) expect(r.labels).toEqual((c.expected as { labels: string[] }).labels)
      else expect(r.error).toBe((c.expected as { error: string }).error)
    })
  }
})

describe('service-vectors.json — priceRules (validatePriceRule, labels)', () => {
  const existing = vectors.priceRules.existing as (PriceRuleDraft & { id: string })[]
  for (const c of vectors.priceRules.cases) {
    it(c.id, () => {
      const r = validatePriceRule(c.rule as PriceRuleDraft, existing)
      expect(r.ok).toBe(c.expected.ok)
      if (!r.ok) {
        expect(r.error).toBe((c.expected as { error: string }).error)
        expect(r.conflictingRuleId).toBe((c.expected as { conflictingRuleId?: string }).conflictingRuleId)
      }
    })
  }
  for (const c of vectors.priceRules.labels) {
    it(c.id, () => {
      expect(priceRuleLabel(c.rule, 'guest')).toBe(c.expectedGuest)
      expect(priceRuleLabel(c.rule, 'staff')).toBe(c.expectedStaff)
    })
  }
})

describe('service-vectors.json — price (hourPrices)', () => {
  const sets = vectors.price.ruleSets as Record<string, PriceRuleLike[]>
  for (const c of vectors.price.cases) {
    it(c.id, () => expect(hourPrices(sets[c.ruleSet], c.businessDate, c.startMinute, c.hours)).toEqual(c.expected))
  }
})

describe('service-vectors.json — money (quoteServiceMoney)', () => {
  for (const c of vectors.money.cases) {
    it(c.id, () => {
      const prices = c.input.hourPrices as number[]
      expect(quoteServiceMoney(prices, c.input.items as MoneyItem[], c.input.prepayPercent)).toEqual(c.expected)
    })
  }
})

describe('service-vectors.json — format (ServiceTimeFormat)', () => {
  for (const c of vectors.format.cases) {
    it(c.id, () => {
      expect(guestTimeLabel(c.businessDate, c.startMinute, c.hours)).toBe(c.expectedGuest)
      expect(staffTimeLabel(c.businessDate, c.startMinute, c.hours)).toBe(c.expectedStaff)
      expect(startLabel(c.businessDate, c.startMinute)).toBe(c.expectedStartLabel)
    })
  }
  for (const c of vectors.format.businessDateLabel) {
    it(c.id, () => expect(businessDateLabel(c.businessDate)).toBe(c.expected))
  }

  it('the guest wording never carries the word «бизнес-день» (ЮР39-8)', () => {
    for (const c of vectors.format.cases) expect(guestTimeLabel(c.businessDate, c.startMinute, c.hours).toLowerCase()).not.toContain('бизнес')
  })
})
