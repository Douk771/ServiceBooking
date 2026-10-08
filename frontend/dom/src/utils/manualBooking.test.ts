// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { manualFieldOfError, toManualInput, validateManual, type ManualForm } from './manualBooking'

const ok: ManualForm = {
  houseId: 'h1',
  checkIn: '2027-03-01',
  checkOut: '2027-03-03',
  adults: 2,
  children: 0,
  dogs: 0,
  needCot: false,
  guestName: ' Пётр ',
  guestPhone: '',
  notifyGuest: false,
  totalOverrideRub: NaN,
  comment: '',
}

describe('manual booking form (§37.30.5)', () => {
  it('valid without a phone: no notification, calculated total', () => {
    expect(validateManual(ok)).toEqual({})
    expect(toManualInput(ok)).toMatchObject({ guestName: 'Пётр', guestPhone: null, notifyGuest: false, totalOverrideRub: null, comment: null })
  })

  it('«notify the guest» needs a phone — the server text', () => {
    expect(validateManual({ ...ok, notifyGuest: true }).guestPhone).toBe('Чтобы уведомить гостя, укажите телефон')
    expect(validateManual({ ...ok, notifyGuest: true, guestPhone: '79001234567' })).toEqual({})
    expect(toManualInput({ ...ok, notifyGuest: true, guestPhone: '79001234567' }).notifyGuest).toBe(true)
  })

  it('a foreign or broken phone is refused', () => {
    expect(validateManual({ ...ok, guestPhone: '123' }).guestPhone).toBe('Введите номер телефона в формате +7 (900) 000-00-00')
  })

  it('dates and name', () => {
    expect(validateManual({ ...ok, checkOut: '2027-03-01' }).dates).toBe('Дата выезда должна быть позже даты заезда')
    expect(validateManual({ ...ok, checkIn: '' }).dates).toBe('Укажите даты заезда и выезда')
    expect(validateManual({ ...ok, guestName: ' ' }).guestName).toBe('Укажите имя')
    expect(validateManual({ ...ok, houseId: '' }).houseId).toBe('Выберите дом')
  })

  it('a hand-made total: 0…10 000 000 whole rubles, replaces the calculation', () => {
    expect(validateManual({ ...ok, totalOverrideRub: 0 })).toEqual({})
    expect(validateManual({ ...ok, totalOverrideRub: 10_000_000 })).toEqual({})
    expect(validateManual({ ...ok, totalOverrideRub: 10_000_001 }).totalOverrideRub).toBe('Итог — от 0 до 10 000 000 ₽')
    expect(validateManual({ ...ok, totalOverrideRub: -1 }).totalOverrideRub).toBe('Итог — от 0 до 10 000 000 ₽')
    expect(validateManual({ ...ok, totalOverrideRub: 12.5 }).totalOverrideRub).toBeTruthy()
    expect(toManualInput({ ...ok, totalOverrideRub: 12000 }).totalOverrideRub).toBe(12000)
  })

  it('routes a 400 to its field', () => {
    expect(manualFieldOfError('Чтобы уведомить гостя, укажите телефон')).toBe('guestPhone')
    expect(manualFieldOfError('Укажите имя')).toBe('guestName')
    expect(manualFieldOfError('Итог — от 0 до 10 000 000 ₽')).toBe('totalOverrideRub')
    expect(manualFieldOfError('другое')).toBeNull()
  })
})
