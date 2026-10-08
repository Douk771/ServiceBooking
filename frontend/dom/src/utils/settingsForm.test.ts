// @vitest-environment node
import { describe, it, expect } from 'vitest'
import {
  CANCELLATION_TEMPLATES,
  HALF_HOURS,
  PREPAY_ZERO_WARNING,
  providerFieldOfError,
  settingsFieldOfError,
  validateProvider,
  validateSettings,
  type ProviderForm,
} from './settingsForm'
import type { StaysSettingsDto } from '../types'

const ok: StaysSettingsDto = {
  checkInTime: '14:00',
  checkOutTime: '12:00',
  minNights: 1,
  maxNights: 30,
  horizonDays: 365,
  allowGapFill: false,
  allowSameDayCheckIn: true,
  holdMinutes: 30,
  prepayPercent: 30,
  cancellationPolicy: 'Standard',
  dogFeeRub: 0,
  cotFeeRub: 0,
  checkInInfoSendTime: '09:00',
  checkInInfoText: null,
  checkInInfoSendFullText: false,
  arrivalReminderEnabled: true,
  housekeeperSeesGuestComment: false,
  showInCatalog: true,
}

describe('settings validation — the texts of the server (§37.27.3)', () => {
  it('defaults are valid', () => {
    expect(validateSettings(ok)).toEqual({})
  })

  it('times are half hours and check-out is not later than check-in', () => {
    expect(validateSettings({ ...ok, checkInTime: '14:15' }).checkInTime).toBe('Время — с шагом 30 минут')
    expect(validateSettings({ ...ok, checkOutTime: '15:00' }).checkOutTime).toBe('Время выезда не может быть позже времени заезда')
    expect(validateSettings({ ...ok, checkOutTime: '14:00' })).toEqual({})
  })

  it.each([
    [{ minNights: 0 }, 'minNights', 'Минимум ночей — от 1 до 30'],
    [{ minNights: 31 }, 'minNights', 'Минимум ночей — от 1 до 30'],
    [{ maxNights: 91 }, 'maxNights', 'Максимум ночей — от 1 до 90'],
    [{ minNights: 10, maxNights: 5 }, 'minNights', 'Минимум не может быть больше максимума'],
    [{ horizonDays: 29 }, 'horizonDays', 'Горизонт бронирования — от 30 до 730 дней'],
    [{ horizonDays: 731 }, 'horizonDays', 'Горизонт бронирования — от 30 до 730 дней'],
    [{ holdMinutes: 9 }, 'holdMinutes', 'Время на оплату — от 10 до 180 минут'],
    [{ holdMinutes: 181 }, 'holdMinutes', 'Время на оплату — от 10 до 180 минут'],
    [{ prepayPercent: -1 }, 'prepayPercent', 'Предоплата — от 0 до 100 %'],
    [{ prepayPercent: 101 }, 'prepayPercent', 'Предоплата — от 0 до 100 %'],
    [{ dogFeeRub: 100_001 }, 'dogFeeRub', 'Сумма — от 0 до 100 000 ₽'],
    [{ cotFeeRub: -5 }, 'cotFeeRub', 'Сумма — от 0 до 100 000 ₽'],
    [{ checkInInfoText: 'а'.repeat(2001) }, 'checkInInfoText', 'Текст к заселению — не длиннее 2000 символов'],
  ] as const)('%j → %s', (patch, field, text) => {
    expect(validateSettings({ ...ok, ...patch })[field]).toBe(text)
  })

  it('boundaries are allowed', () => {
    expect(validateSettings({ ...ok, minNights: 30, maxNights: 90, horizonDays: 730, holdMinutes: 180, prepayPercent: 100, dogFeeRub: 100_000 })).toEqual({})
    expect(validateSettings({ ...ok, minNights: 1, maxNights: 1, horizonDays: 30, holdMinutes: 10, prepayPercent: 0 })).toEqual({})
  })

  it('half-hour list covers the day', () => {
    expect(HALF_HOURS).toHaveLength(48)
    expect(HALF_HOURS[0]).toBe('00:00')
    expect(HALF_HOURS[47]).toBe('23:30')
  })

  it('routes a server 400 to its field', () => {
    expect(settingsFieldOfError('Время выезда не может быть позже времени заезда')).toBe('checkOutTime')
    expect(settingsFieldOfError('Минимум не может быть больше максимума')).toBe('minNights')
    expect(settingsFieldOfError('Предоплата — от 0 до 100 %')).toBe('prepayPercent')
    expect(settingsFieldOfError('что-то другое')).toBeNull()
  })
})

describe('cancellation templates (ЮР-1)', () => {
  it('exactly the three lawful ones, no percentages of the stay, no forbidden words', () => {
    expect(CANCELLATION_TEMPLATES.map((t) => t.value)).toEqual(['Standard', 'Flexible', 'NoDeductions'])
    expect(CANCELLATION_TEMPLATES.map((t) => t.title)).toEqual(['Стандартный', 'Гибкий', 'Без удержаний'])
    const all = CANCELLATION_TEMPLATES.map((t) => `${t.title} ${t.text}`).join(' ').toLowerCase()
    expect(all).not.toMatch(/задат|невозвратн|депозит|50 ?%|мягкий|строгий/)
  })

  it('every deduction is capped at the first night, never more', () => {
    for (const t of CANCELLATION_TEMPLATES.filter((x) => x.value !== 'NoDeductions')) expect(t.text).toContain('не больше стоимости первой ночи')
  })

  it('the zero-prepayment warning says guests can book without paying and that only captcha and limits protect', () => {
    expect(PREPAY_ZERO_WARNING).toContain('без оплаты')
    expect(PREPAY_ZERO_WARNING).toContain('капча и лимиты')
  })
})

describe('provider form (ЮР-3, §37.27.5)', () => {
  const org: ProviderForm = { status: 'Organization', name: 'ООО Лесные дома', inn: '1234567890', ogrn: '1234567890123', claimsAddress: 'Новокузнецк, ул. Мира, 1' }

  it('organization: INN 10 digits, OGRN 13', () => {
    expect(validateProvider(org)).toEqual({})
    expect(validateProvider({ ...org, inn: '123456789012' }).inn).toBe('Неверный ИНН')
    expect(validateProvider({ ...org, ogrn: '' }).ogrn).toBe('Укажите ОГРН')
    expect(validateProvider({ ...org, ogrn: '123' }).ogrn).toBe('Неверный ОГРН')
  })

  it('sole proprietor: INN 12, OGRNIP 15 required', () => {
    const ip: ProviderForm = { ...org, status: 'IndividualEntrepreneur', inn: '123456789012', ogrn: '123456789012345' }
    expect(validateProvider(ip)).toEqual({})
    expect(validateProvider({ ...ip, ogrn: '' }).ogrn).toBe('Укажите ОГРНИП')
    expect(validateProvider({ ...ip, ogrn: '1234567890123' }).ogrn).toBe('Неверный ОГРН')
  })

  it('self-employed and individual: INN 12, no OGRN asked', () => {
    for (const status of ['SelfEmployed', 'Individual'] as const) {
      expect(validateProvider({ ...org, status, inn: '123456789012', ogrn: '' })).toEqual({})
      expect(validateProvider({ ...org, status, inn: '1234567890', ogrn: '' }).inn).toBe('Неверный ИНН')
    }
  })

  it('status, name and claims address are required', () => {
    expect(validateProvider({ ...org, status: '' }).status).toBeTruthy()
    expect(validateProvider({ ...org, name: ' ' }).name).toBeTruthy()
    expect(validateProvider({ ...org, claimsAddress: '' }).claimsAddress).toBe('Укажите адрес для претензий')
  })

  it('routes a server 400 to its field', () => {
    expect(providerFieldOfError('Неверный ИНН')).toBe('inn')
    expect(providerFieldOfError('Укажите ОГРНИП')).toBe('ogrn')
    expect(providerFieldOfError('ОГРН указывается только для организации и ИП')).toBe('ogrn')
    expect(providerFieldOfError('Укажите адрес для претензий')).toBe('claimsAddress')
  })
})
