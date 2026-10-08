// @vitest-environment node
import { describe, it, expect } from 'vitest'
import {
  extraBedsNeeded,
  fieldOfBookingError,
  guestCountsProblem,
  maxGuests,
  toCreateInput,
  toQuoteInput,
  validateGuestFields,
  type GuestFields,
} from './bookingForm'
import { bookingKeyFor, forgetBookingKey, newIdempotencyKey } from './idempotency'

const house = { capacity: 4, extraBeds: { enabled: true, max: 2, priceRub: 800 }, dogsForbidden: false, hasCot: true }
const counts = { adults: 2, children: 0, dogs: 0, needCot: false }
const guest: GuestFields = { name: ' Анна ', phone: '79001234567', arrivalTime: '18:00', comment: ' привет ', notifyByMessenger: false }

describe('guest counts', () => {
  it('house takes capacity plus the extra beds, only when enabled', () => {
    expect(maxGuests(house)).toBe(6)
    expect(maxGuests({ capacity: 4, extraBeds: { enabled: false, max: 3, priceRub: 0 } })).toBe(4)
    expect(extraBedsNeeded(house, { adults: 4, children: 2 })).toBe(2)
    expect(extraBedsNeeded(house, { adults: 1, children: 1 })).toBe(0)
  })

  it('says the server wording for a count the house cannot take', () => {
    expect(guestCountsProblem(house, { ...counts, adults: 5, children: 2 })).toBe('В доме помещается не больше 6 гостей, включая доп. места')
    expect(guestCountsProblem({ ...house, extraBeds: { enabled: false, max: 0, priceRub: 0 } }, { ...counts, adults: 5 })).toBe(
      'В доме помещается не больше 4 гостей',
    )
    expect(guestCountsProblem({ ...house, dogsForbidden: true }, { ...counts, dogs: 1 })).toBe('В этом доме нельзя проживать с собаками')
    expect(guestCountsProblem({ ...house, hasCot: false }, { ...counts, needCot: true })).toBe('В этом доме нет детской кроватки')
    expect(guestCountsProblem(house, { ...counts, adults: 6 })).toBeNull()
  })
})

describe('guest fields', () => {
  const anon = { anonymous: true, captchaRequired: true, captchaToken: 'tok' }

  it('anonymous guest needs a name, a Russian phone and the captcha', () => {
    expect(validateGuestFields(guest, anon)).toEqual({})
    expect(validateGuestFields({ ...guest, name: '  ', phone: '123' }, anon)).toEqual({
      name: 'Укажите имя',
      phone: 'Введите номер телефона в формате +7 (900) 000-00-00',
    })
    expect(validateGuestFields(guest, { ...anon, captchaToken: '' }).captcha).toBe('Подтвердите, что вы не робот')
    expect(validateGuestFields(guest, { ...anon, captchaRequired: false, captchaToken: '' })).toEqual({})
  })

  it('a signed-in guest is not asked for a phone or a captcha', () => {
    expect(validateGuestFields({ ...guest, phone: '' }, { anonymous: false, captchaRequired: true, captchaToken: '' })).toEqual({})
  })

  it('name and comment limits', () => {
    expect(validateGuestFields({ ...guest, name: 'а'.repeat(101) }, anon).name).toBe('Имя — не длиннее 100 символов')
    expect(validateGuestFields({ ...guest, comment: 'а'.repeat(501) }, anon).comment).toBe('Комментарий — не длиннее 500 символов')
  })
})

describe('request bodies', () => {
  it('quote input carries the dates and the counts', () => {
    expect(toQuoteInput('2027-01-05', '2027-01-08', { adults: 2, children: 1, dogs: 1, needCot: true })).toEqual({
      checkIn: '2027-01-05',
      checkOut: '2027-01-08',
      adults: 2,
      children: 1,
      dogs: 1,
      needCot: true,
    })
  })

  it('anonymous booking sends the phone, the captcha and the total the guest saw; text is trimmed', () => {
    const body = toCreateInput({
      checkIn: '2027-01-05',
      checkOut: '2027-01-08',
      counts,
      guest,
      anonymous: true,
      expectedTotalRub: 15000,
      idempotencyKey: 'k1',
      captchaToken: 'tok',
    })
    expect(body).toMatchObject({
      guestName: 'Анна',
      guestPhone: '79001234567',
      comment: 'привет',
      arrivalTime: '18:00',
      notifyByMessenger: false,
      expectedTotalRub: 15000,
      idempotencyKey: 'k1',
      captchaToken: 'tok',
    })
  })

  it('a signed-in booking sends no phone and no captcha; an empty comment and «не знаю» become null', () => {
    const body = toCreateInput({
      checkIn: '2027-01-05',
      checkOut: '2027-01-08',
      counts,
      guest: { ...guest, comment: '  ', arrivalTime: '' },
      anonymous: false,
      expectedTotalRub: 15000,
      idempotencyKey: 'k1',
      captchaToken: 'ignored',
    })
    expect(body.guestPhone).toBeNull()
    expect(body.captchaToken).toBeNull()
    expect(body.comment).toBeNull()
    expect(body.arrivalTime).toBeNull()
  })

  it('the messenger consent is never on by itself', () => {
    expect(toCreateInput({ checkIn: 'a', checkOut: 'b', counts, guest: { ...guest, notifyByMessenger: false }, anonymous: true, expectedTotalRub: 1, idempotencyKey: 'k', captchaToken: '' }).notifyByMessenger).toBe(false)
  })
})

describe('400 text → field', () => {
  it.each([
    ['Укажите имя', 'name'],
    ['Имя — не длиннее 100 символов', 'name'],
    ['Введите номер телефона в формате +7 (900) 000-00-00', 'phone'],
    ['Комментарий — не длиннее 500 символов', 'comment'],
    ['Подтвердите, что вы не робот', 'captcha'],
    ['Время прибытия — от времени заезда до 23:30 с шагом 30 минут', 'arrival'],
    ['Неверный формат даты', 'dates'],
    ['Укажите даты заезда и выезда', 'dates'],
    ['Взрослых — от 1 до 30', 'guests'],
    ['Собак — от 0 до 20', 'guests'],
  ])('%s → %s', (text, field) => {
    expect(fieldOfBookingError(text)).toBe(field)
  })
  it('unknown text stays a form-level error', () => {
    expect(fieldOfBookingError('Нужен ключ запроса — обновите страницу')).toBeNull()
  })
})

describe('idempotency key', () => {
  const memory = () => {
    const m = new Map<string, string>()
    return { getItem: (k: string) => m.get(k) ?? null, setItem: (k: string, v: string) => void m.set(k, v), removeItem: (k: string) => void m.delete(k) }
  }

  it('looks like a uuid and differs between calls', () => {
    const a = newIdempotencyKey()
    expect(a).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(newIdempotencyKey()).not.toBe(a)
  })

  it('is kept for the house until the booking succeeds, then renewed', () => {
    const s = memory()
    const first = bookingKeyFor('h1', s)
    expect(bookingKeyFor('h1', s)).toBe(first) // a refresh or a retry reuses it
    expect(bookingKeyFor('h2', s)).not.toBe(first) // another house, another booking
    forgetBookingKey('h1', s)
    expect(bookingKeyFor('h1', s)).not.toBe(first)
  })

  it('works without storage (private mode)', () => {
    expect(bookingKeyFor('h1', null)).toMatch(/-/)
  })
})
