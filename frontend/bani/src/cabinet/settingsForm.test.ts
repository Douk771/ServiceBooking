import { describe, it, expect } from 'vitest'
import { formToSettingsInput, settingsFieldOfError, settingsToForm, validateSettings, REMINDER_HOURS_DEFAULT, type SettingsForm } from './settingsForm'

const base = { horizonDays: 90, holdMinutes: 60, housekeeperSeesGuestComment: false, showInCatalog: true }
const form = (over: Partial<SettingsForm> = {}): SettingsForm => ({ ...base, sessionReminderEnabled: true, sessionReminderHours: 3, ...over })

describe('settingsToForm', () => {
  it('keeps the hours the server sent while the reminder is on', () => {
    expect(settingsToForm({ ...base, sessionReminderEnabled: true, sessionReminderHours: 5 }).sessionReminderHours).toBe(5)
  })

  it('falls back to the default when the reminder is off: the server keeps no hours then (NULL)', () => {
    expect(settingsToForm({ ...base, sessionReminderEnabled: false, sessionReminderHours: null }).sessionReminderHours).toBe(REMINDER_HOURS_DEFAULT)
    expect(settingsToForm({ ...base, sessionReminderEnabled: false, sessionReminderHours: 0 }).sessionReminderHours).toBe(REMINDER_HOURS_DEFAULT)
    expect(settingsToForm({ ...base, sessionReminderEnabled: false }).sessionReminderHours).toBe(REMINDER_HOURS_DEFAULT)
  })

  it('does not trust an out-of-range number even with the reminder on', () => {
    expect(settingsToForm({ ...base, sessionReminderEnabled: true, sessionReminderHours: 99 }).sessionReminderHours).toBe(REMINDER_HOURS_DEFAULT)
  })
})

describe('formToSettingsInput', () => {
  it('sends the typed hours when the reminder is on', () => {
    expect(formToSettingsInput(form({ sessionReminderHours: 8 }))).toMatchObject({ sessionReminderEnabled: true, sessionReminderHours: 8 })
  })

  it('sends the default hours (not a leftover) when the reminder is off', () => {
    expect(formToSettingsInput(form({ sessionReminderEnabled: false, sessionReminderHours: 17 }))).toMatchObject({
      sessionReminderEnabled: false,
      sessionReminderHours: REMINDER_HOURS_DEFAULT,
    })
  })
})

describe('validateSettings', () => {
  it('passes a valid form', () => {
    expect(validateSettings(form())).toEqual({})
  })

  it('checks the horizon and the time to pay with the texts of the contract', () => {
    expect(validateSettings(form({ horizonDays: 29 })).horizonDays).toBe('Горизонт бронирования — от 30 до 730 дней')
    expect(validateSettings(form({ horizonDays: 731 })).horizonDays).toBeDefined()
    expect(validateSettings(form({ holdMinutes: 9 })).holdMinutes).toBe('Время на оплату — от 10 до 180 минут')
    expect(validateSettings(form({ holdMinutes: 181 })).holdMinutes).toBeDefined()
    expect(validateSettings(form({ horizonDays: 30.5 })).horizonDays).toBeDefined()
  })

  it('checks the hours only while the reminder is on', () => {
    expect(validateSettings(form({ sessionReminderHours: 0 })).sessionReminderHours).toBeDefined()
    expect(validateSettings(form({ sessionReminderHours: 25 })).sessionReminderHours).toBeDefined()
    expect(validateSettings(form({ sessionReminderEnabled: false, sessionReminderHours: 0 }))).toEqual({})
  })
})

describe('settingsFieldOfError', () => {
  it('routes the 400 texts of PUT …/settings to their fields', () => {
    expect(settingsFieldOfError('Горизонт бронирования — от 30 до 730 дней')).toBe('horizonDays')
    expect(settingsFieldOfError('Время на оплату — от 10 до 180 минут')).toBe('holdMinutes')
    expect(settingsFieldOfError('Напоминание — за 1…24 часа до начала')).toBe('sessionReminderHours')
    expect(settingsFieldOfError('Что-то другое')).toBeNull()
  })
})
