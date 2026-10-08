// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { REMINDER_TIME_OPTIONS, insertAtCursor, isDefaultDraft, templateForSave } from './reminderTemplate'

describe('REMINDER_TIME_OPTIONS', () => {
  it('runs from 08:00 to 22:00 in half hours (§39.33.5)', () => {
    expect(REMINDER_TIME_OPTIONS[0]).toBe('08:00')
    expect(REMINDER_TIME_OPTIONS[REMINDER_TIME_OPTIONS.length - 1]).toBe('22:00')
    expect(REMINDER_TIME_OPTIONS).toHaveLength(29)
    expect(REMINDER_TIME_OPTIONS).toContain('18:30')
  })
})

describe('insertAtCursor', () => {
  it('inserts at the cursor and moves it behind the token', () => {
    expect(insertAtCursor('Привет, !', 8, 8, '{ИмяГостя}')).toEqual({ text: 'Привет, {ИмяГостя}!', cursor: 18 })
  })
  it('replaces the selection', () => {
    expect(insertAtCursor('abcXXXdef', 3, 6, '{Дом}')).toEqual({ text: 'abc{Дом}def', cursor: 8 })
  })
  it('clamps a cursor outside the text', () => {
    expect(insertAtCursor('ab', 10, 20, '{x}').text).toBe('ab{x}')
  })
})

describe('templateForSave', () => {
  const def = 'Завтра заезд в «{Дом}»'
  it('stores the default (and an empty text) as null', () => {
    expect(templateForSave(def, def)).toBeNull()
    expect(templateForSave('   ', def)).toBeNull()
    expect(templateForSave(def.replace(/\n/g, '\r\n'), def)).toBeNull()
  })
  it('keeps a changed text as typed', () => {
    expect(templateForSave('Ждём вас!', def)).toBe('Ждём вас!')
  })
  it('tells a default draft from a changed one', () => {
    expect(isDefaultDraft(def, def)).toBe(true)
    expect(isDefaultDraft('Ждём вас!', def)).toBe(false)
  })
})
