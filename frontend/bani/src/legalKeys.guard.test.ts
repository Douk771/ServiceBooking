// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { bathsVertical } from './vertical'
import { BATH_FALLBACKS } from './utils/baniTexts'

// FE-42-8 / Т42-04 / API_CONTRACT_CYCLE42.md §42.37.3 (CY42-F12): bani never asks for the legal keys of houses. The names of the keys are
// inside; the texts of a bath company about a house, a stay, a tourist tax would be untrue. The list below is the contract's, verbatim.
const FORBIDDEN_KEYS = [
  'StayPublicContactsNotice',
  'StayTouristTaxNotice',
  'StayRegistryOwnerNotice',
  'StayMigrationOwnerNotice',
  'StayCheckInInfoOwnerNotice',
  'StayServiceAddNotice',
  'StayPaymentProofNotice',
  'StayPaymentRequisitesOwnerNotice',
  'StayOwnerCancelNotice',
]
// prefixes: `StayReminder*` and `StayBooking*` (StayBookingNotice, StayBookingTerms, StayBookingCancellation…)
const FORBIDDEN_PREFIXES = ['StayReminder', 'StayBooking']
// The contract's «as in dom» keys (§42.37.3): about a service, not a house, so a bath company may show them.
const REUSED_KEYS = [
  'StayServiceCancellationTerms',
  'StayServiceCommentNotice',
  'StayServiceCancellationOwnerNotice',
  'StayMessengerConsent',
  'StayServiceSafetyOwnerNotice',
]

const isForbidden = (key: string) => FORBIDDEN_KEYS.includes(key) || FORBIDDEN_PREFIXES.some((p) => key.startsWith(p))
const isAllowed = (key: string) => key.startsWith('Bath') || REUSED_KEYS.includes(key) || !key.startsWith('Stay')

const raw = import.meta.glob(['./**/*.{ts,tsx}'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>
// comments may name the keys (to explain the ban); code and strings may not
const strip = (src: string) => src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"`])\/\/.*$/gm, '$1')
const SOURCES = Object.entries(raw)
  .filter(([p]) => !/\.test\.tsx?$/.test(p))
  .map(([p, src]) => [p, strip(src)] as const)

describe('legal keys of bani (Т42-04)', () => {
  it('scans the sources of bani', () => {
    expect(SOURCES.length).toBeGreaterThan(30)
    expect(SOURCES.some(([p]) => p === './vertical.ts')).toBe(true)
  })

  it('no module of bani mentions a key of houses', () => {
    const rx = new RegExp(`['"\`](${FORBIDDEN_KEYS.join('|')}|${FORBIDDEN_PREFIXES.join('|')}[A-Za-z]*)['"\`]|\\b(${FORBIDDEN_KEYS.join('|')})\\b`)
    const offenders = SOURCES.filter(([, src]) => rx.test(src)).map(([p]) => p)
    expect(offenders).toEqual([])
  })

  it('the keys the vertical asks for are Bath… keys, the contract\'s reused ones, or not Stay… at all', () => {
    const asked = Object.values(bathsVertical.legal.keys).filter((k): k is string => typeof k === 'string')
    expect(asked.length).toBeGreaterThan(5)
    expect(asked.filter(isForbidden)).toEqual([])
    expect(asked.filter((k) => !isAllowed(k))).toEqual([])
  })

  it('the fallbacks carry the same: no forbidden key, every Bath… key of the contract is there', () => {
    const keys = Object.keys(BATH_FALLBACKS)
    expect(keys.filter(isForbidden)).toEqual([])
    expect(keys.filter((k) => !isAllowed(k))).toEqual([])
    for (const k of [
      'BathBookingNotice',
      'BathBookingTerms',
      'BathPublicContactsNotice',
      'BathPositionsOwnerNotice',
      'BathCapacityOwnerNotice',
      'BathPaymentProofNotice',
      'BathPaymentRequisitesOwnerNotice',
      'BathOwnerCancelNotice',
    ])
      expect(keys).toContain(k)
  })

  it('detects a key of houses (the guard is not vacuous)', () => {
    expect(isForbidden('StayPublicContactsNotice')).toBe(true)
    expect(isForbidden('StayReminderEveningNotice')).toBe(true)
    expect(isForbidden('StayBookingTerms')).toBe(true)
    expect(isForbidden('StayServiceBookingTerms')).toBe(false)
    expect(isAllowed('StayServiceAddNotice')).toBe(false)
  })
})
