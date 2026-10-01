// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { assortmentDate, defaultChoice, parsePickupState, requestedDate, isChoiceStillOffered, nextRadioIndex, parsePickup, sameSlot, toPickupInput } from './pickup'
import type { PickupOptionsDto } from '../types'

const options = (over: Partial<PickupOptionsDto> = {}): PickupOptionsDto => ({
  asapEnabled: true,
  scheduledEnabled: true,
  asap: { available: true, text: '≈ к 13:20', readyAtUtc: '2026-09-30T10:20:00Z' },
  dates: [
    { date: '2026-09-30', label: 'Сегодня', hasSlots: true },
    { date: '2026-10-01', label: 'Завтра', hasSlots: false, reasonText: 'нет слотов' },
  ],
  ...over,
})

describe('pickup choice', () => {
  it('parses only well-formed stored values', () => {
    expect(parsePickup('{"kind":"Asap"}')).toEqual({ kind: 'Asap' })
    expect(parsePickup('{"kind":"Slot","date":"2026-10-02","slotStartUtc":"2026-10-02T09:00:00Z"}')).toEqual({ kind: 'Slot', date: '2026-10-02', slotStartUtc: '2026-10-02T09:00:00Z' })
    expect(parsePickup('{"kind":"Slot","date":"02.10","slotStartUtc":"x"}')).toBeNull()
    expect(parsePickup('{"kind":"Slot","date":"2026-10-02"}')).toBeNull()
    expect(parsePickup('not json')).toBeNull()
    expect(parsePickup(null)).toBeNull()
  })

  it('builds the wire shape: Asap has no date, Slot carries both; nothing chosen = Asap (server default)', () => {
    expect(toPickupInput(null)).toEqual({ kind: 'Asap' })
    expect(toPickupInput({ kind: 'Slot', date: '2026-10-02', slotStartUtc: '2026-10-02T09:00:00Z' })).toEqual({ kind: 'Slot', date: '2026-10-02', slotStartUtc: '2026-10-02T09:00:00Z' })
  })

  it('asks the storefront for the slot date only when a slot is chosen', () => {
    expect(assortmentDate(null)).toBeUndefined()
    expect(assortmentDate({ kind: 'Asap' })).toBeUndefined()
    expect(assortmentDate({ kind: 'Slot', date: '2026-10-02', slotStartUtc: 's' })).toBe('2026-10-02')
  })

  it('drops a saved choice the shop no longer offers', () => {
    expect(isChoiceStillOffered(null, options())).toBe(true)
    expect(isChoiceStillOffered({ kind: 'Asap' }, options())).toBe(true)
    expect(isChoiceStillOffered({ kind: 'Asap' }, options({ asapEnabled: false }))).toBe(false)
    expect(isChoiceStillOffered({ kind: 'Slot', date: '2026-09-30', slotStartUtc: 's' }, options())).toBe(true)
    expect(isChoiceStillOffered({ kind: 'Slot', date: '2026-10-01', slotStartUtc: 's' }, options())).toBe(false) // date without slots
    expect(isChoiceStillOffered({ kind: 'Slot', date: '2026-09-30', slotStartUtc: 's' }, options({ scheduledEnabled: false, dates: [] }))).toBe(false)
  })

  it('preselects «как можно скорее» only when it is possible right now', () => {
    expect(defaultChoice(options())).toEqual({ kind: 'Asap' })
    expect(defaultChoice(options({ asap: { available: false, text: 'Сегодня уже не успеем приготовить заказ' } }))).toBeNull()
    expect(defaultChoice(options({ asapEnabled: false, asap: null }))).toBeNull()
  })

  it('recognises the chosen slot by its exact start', () => {
    const c = { kind: 'Slot', date: '2026-09-30', slotStartUtc: 'A' } as const
    expect(sameSlot(c, '2026-09-30', { startUtc: 'A' })).toBe(true)
    expect(sameSlot(c, '2026-09-30', { startUtc: 'B' })).toBe(false)
    expect(sameSlot(c, '2026-10-01', { startUtc: 'A' })).toBe(false)
  })
})

describe('nextRadioIndex (radiogroup keyboard, SPEC §6)', () => {
  it('moves with arrows and wraps around', () => {
    expect(nextRadioIndex('ArrowRight', 2, 3)).toBe(0)
    expect(nextRadioIndex('ArrowDown', 0, 3)).toBe(1)
    expect(nextRadioIndex('ArrowLeft', 0, 3)).toBe(2)
    expect(nextRadioIndex('ArrowUp', 1, 3)).toBe(0)
  })
  it('supports Home/End and ignores other keys and empty groups', () => {
    expect(nextRadioIndex('Home', 2, 5)).toBe(0)
    expect(nextRadioIndex('End', 0, 5)).toBe(4)
    expect(nextRadioIndex('a', 0, 5)).toBeNull()
    expect(nextRadioIndex('ArrowRight', 0, 0)).toBeNull()
  })
})

describe('pickup state (choice + browsed date)', () => {
  it('reads the envelope defensively and falls back to «nothing chosen»', () => {
    expect(parsePickupState(null)).toEqual({ choice: null, browseDate: null })
    expect(parsePickupState('garbage')).toEqual({ choice: null, browseDate: null })
    expect(parsePickupState('{"choice":{"kind":"Asap"},"browseDate":"2026-10-02"}')).toEqual({ choice: { kind: 'Asap' }, browseDate: '2026-10-02' })
    expect(parsePickupState('{"choice":{"kind":"Slot","date":"x"},"browseDate":"тест"}')).toEqual({ choice: null, browseDate: null })
  })
  it('keeps the server labels of a slot for the cart summary', () => {
    const raw = '{"choice":{"kind":"Slot","date":"2026-10-02","slotStartUtc":"S","dateLabel":"пт 2 окт","slotLabel":"12:00–12:15"},"browseDate":"2026-10-02"}'
    expect(parsePickupState(raw).choice).toEqual({ kind: 'Slot', date: '2026-10-02', slotStartUtc: 'S', dateLabel: 'пт 2 окт', slotLabel: '12:00–12:15' })
  })
  it('requests the browsed date first (the assortment follows the date even before a slot is picked)', () => {
    expect(requestedDate({ choice: null, browseDate: '2026-10-03' })).toBe('2026-10-03')
    expect(requestedDate({ choice: { kind: 'Slot', date: '2026-10-02', slotStartUtc: 'S' }, browseDate: null })).toBe('2026-10-02')
    expect(requestedDate({ choice: { kind: 'Asap' }, browseDate: null })).toBeUndefined()
  })
})
