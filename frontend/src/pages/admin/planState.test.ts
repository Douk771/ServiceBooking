import { describe, it, expect } from 'vitest'
import { planStateLabel } from './planState'

// ARCHITECTURE_CYCLE15.md §255.2 — the three-state table, computed as a pure function so a second
// copy of this logic inline in JSX would be a regression.
describe('planStateLabel', () => {
  it('isActive: true, isPublic: true → «На витрине»', () => {
    expect(planStateLabel({ isActive: true, isPublic: true })).toEqual({ label: 'На витрине', tone: 'public' })
  })

  it('isActive: true, isPublic: false → «Скрыт с витрины»', () => {
    expect(planStateLabel({ isActive: true, isPublic: false })).toEqual({
      label: 'Скрыт с витрины',
      tone: 'hidden',
    })
  })

  it('isActive: false → «Архивный», regardless of isPublic', () => {
    expect(planStateLabel({ isActive: false, isPublic: true })).toEqual({ label: 'Архивный', tone: 'archived' })
    expect(planStateLabel({ isActive: false, isPublic: false })).toEqual({ label: 'Архивный', tone: 'archived' })
  })
})
