// @vitest-environment node
import { describe, it, expect } from 'vitest'
import {
  DIALOG_CLOSES,
  OBJECT_KINDS,
  publishProblemText,
  validateContent,
  validatePeriod,
  validatePrice,
  validateRegistry,
  validateSetup,
} from './houseForms'
import type { HouseSetupInput } from '../types'

const setup: HouseSetupInput = { name: 'Дом у склона', slug: 'dom-1', capacity: 4, extraBedsEnabled: false, extraBedsMax: 0, extraBedPriceRub: 0, dogsForbidden: false, hasCot: false }

describe('house setup (§37.28)', () => {
  it('valid', () => {
    expect(validateSetup(setup)).toEqual({})
    expect(validateSetup({ ...setup, extraBedsEnabled: true, extraBedsMax: 2, extraBedPriceRub: 800 })).toEqual({})
  })
  it('name, slug, capacity', () => {
    expect(validateSetup({ ...setup, name: '  ' }).name).toBe('Укажите название дома')
    expect(validateSetup({ ...setup, name: 'а'.repeat(101) }).name).toBe('Укажите название дома')
    expect(validateSetup({ ...setup, slug: 'Дом' }).slug).toBe('Адрес дома — латиница, цифры и дефис, 2–50 символов')
    expect(validateSetup({ ...setup, capacity: 0 }).capacity).toBe('Вместимость — от 1 до 50')
    expect(validateSetup({ ...setup, capacity: 51 }).capacity).toBe('Вместимость — от 1 до 50')
  })
  it('extra beds are checked only when enabled', () => {
    expect(validateSetup({ ...setup, extraBedsMax: 99 })).toEqual({})
    expect(validateSetup({ ...setup, extraBedsEnabled: true, extraBedsMax: 0 }).extraBedsMax).toBe('Доп. мест — от 1 до 10')
    expect(validateSetup({ ...setup, extraBedsEnabled: true, extraBedsMax: 11 }).extraBedsMax).toBe('Доп. мест — от 1 до 10')
    expect(validateSetup({ ...setup, extraBedsEnabled: true, extraBedsMax: 2, extraBedPriceRub: 100_001 }).extraBedPriceRub).toBe('Сумма — от 0 до 100 000 ₽')
  })
})

describe('content, price, period', () => {
  it('content limits', () => {
    expect(validateContent({ description: '', address: '', checkInInfoText: '' })).toEqual({})
    expect(validateContent({ description: 'а'.repeat(4001), address: 'а'.repeat(501), checkInInfoText: 'а'.repeat(2001) })).toEqual({
      description: 'Описание — не длиннее 4000 символов',
      address: 'Адрес — не длиннее 500 символов',
      checkInInfoText: 'Текст к заселению — не длиннее 2000 символов',
    })
  })

  it('price is whole rubles from 1 to 1 000 000', () => {
    expect(validatePrice(1)).toBeNull()
    expect(validatePrice(1_000_000)).toBeNull()
    for (const bad of [0, -5, 1_000_001, 12.5, NaN]) expect(validatePrice(bad)).toBe('Цена — от 1 до 1 000 000 ₽')
  })

  it('period: order of dates, two years at most, price', () => {
    expect(validatePeriod({ startDate: '2027-01-01', endDate: '2027-01-10', priceRub: 5000 })).toEqual({})
    expect(validatePeriod({ startDate: '2027-01-01', endDate: '2027-01-01', priceRub: 5000 })).toEqual({}) // one day is a period
    expect(validatePeriod({ startDate: '2027-01-10', endDate: '2027-01-01', priceRub: 5000 }).endDate).toBe('Дата окончания не может быть раньше начала')
    expect(validatePeriod({ startDate: '2027-01-01', endDate: '2029-01-02', priceRub: 5000 }).endDate).toBe('Период — не длиннее двух лет')
    expect(validatePeriod({ startDate: '2027-01-01', endDate: '2028-12-31', priceRub: 5000 })).toEqual({}) // 731 days
    expect(validatePeriod({ startDate: '', endDate: '2027-01-10', priceRub: 5000 }).startDate).toBe('Укажите дату начала')
    expect(validatePeriod({ startDate: '2027-01-01', endDate: '2027-01-10', priceRub: 0 }).priceRub).toBe('Цена — от 1 до 1 000 000 ₽')
  })
})

describe('registry (ЮР-2)', () => {
  it('a kind is required; the number is optional (a house without a number is published under the owner’s attestation)', () => {
    expect(validateRegistry({ objectKind: 'Residential', registryNumber: '', registryUrl: '' })).toEqual({})
    expect(validateRegistry({ objectKind: 'GuestHouse', registryNumber: '', registryUrl: '' })).toEqual({})
    expect(validateRegistry({ objectKind: '', registryNumber: '', registryUrl: '' }).objectKind).toBe('Укажите вид объекта')
  })

  it('number 5–32 letters/digits/hyphen, link https only', () => {
    expect(validateRegistry({ objectKind: 'GuestHouse', registryNumber: 'КЕМ-2025-0042', registryUrl: 'https://example.ru/r/42' })).toEqual({})
    expect(validateRegistry({ objectKind: 'GuestHouse', registryNumber: 'abc', registryUrl: '' }).registryNumber).toBeTruthy()
    expect(validateRegistry({ objectKind: 'GuestHouse', registryNumber: 'a'.repeat(33), registryUrl: '' }).registryNumber).toBeTruthy()
    expect(validateRegistry({ objectKind: 'GuestHouse', registryNumber: 'ab cde', registryUrl: '' }).registryNumber).toBeTruthy()
    expect(validateRegistry({ objectKind: 'GuestHouse', registryNumber: '', registryUrl: 'http://x.ru' }).registryUrl).toBe('Ссылка на запись в реестре должна начинаться с https://')
  })

  it('the kinds offered are the three of the contract', () => {
    expect(OBJECT_KINDS.map((k) => k.value)).toEqual(['Residential', 'GuestHouse', 'OtherAccommodation'])
  })
})

describe('publish problems in the server words', () => {
  it('names each blocker', () => {
    expect(publishProblemText('NoPrice')).toBe('Задайте цену: постоянную или хотя бы один период на будущие даты')
    expect(publishProblemText('ObjectKindRequired')).toBe('Укажите вид объекта')
    expect(publishProblemText('RegistryNumberRequired')).toBe('Для гостевого дома и средства размещения укажите номер в реестре')
    expect(publishProblemText('HouseArchived')).toBe('Дом в архиве')
    expect(publishProblemText('SlugTaken')).toBe('SlugTaken') // not a publish blocker: shown as is, never hidden
  })
  it('the attestation is closed by the dialog itself', () => {
    expect(DIALOG_CLOSES).toContain('AttestationRequired')
  })
})
