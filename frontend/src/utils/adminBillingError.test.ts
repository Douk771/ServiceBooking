import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getAdminBillingErrorMessage, isLimitOverflowConflict } from './adminBillingError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data } as any,
  } as AxiosError
}

describe('getAdminBillingErrorMessage', () => {
  it('prints the server bare-string body verbatim for 409 — the server already names what overflowed', () => {
    const err = axiosErrorWith(409, 'Лимит сотрудников (5) меньше уже занятого (7) — подтвердите перерасход')
    expect(getAdminBillingErrorMessage(err)).toBe('Лимит сотрудников (5) меньше уже занятого (7) — подтвердите перерасход')
  })

  it('prints the server body for 400 too', () => {
    const err = axiosErrorWith(400, 'Дата "оплачено до" в прошлом')
    expect(getAdminBillingErrorMessage(err)).toBe('Дата "оплачено до" в прошлом')
  })

  it('falls back when the server sends an empty 409 body', () => {
    const err = axiosErrorWith(409, '')
    expect(getAdminBillingErrorMessage(err, 'резервный текст')).toBe('резервный текст')
  })

  it('maps 403 and 404 to fixed messages regardless of body', () => {
    expect(getAdminBillingErrorMessage(axiosErrorWith(403, ''))).toBe('Недостаточно прав для этого действия.')
    expect(getAdminBillingErrorMessage(axiosErrorWith(404, ''))).toBe('Не найдено — возможно, аккаунт или заявка уже удалены.')
  })
})

describe('isLimitOverflowConflict', () => {
  it('recognises the limit-overflow wording of the three possible 409s', () => {
    const err = axiosErrorWith(409, 'Новый лимит компаний меньше уже занятого')
    expect(isLimitOverflowConflict(err)).toBe(true)
  })

  it('does not flag the "option unavailable on this plan" 409 as an overflow', () => {
    const err = axiosErrorWith(409, 'Опция недоступна на выбранном тарифе')
    expect(isLimitOverflowConflict(err)).toBe(false)
  })

  it('does not flag the "request already processed" 409 as an overflow', () => {
    const err = axiosErrorWith(409, 'Заявка уже обработана')
    expect(isLimitOverflowConflict(err)).toBe(false)
  })

  it('ignores non-409 errors', () => {
    expect(isLimitOverflowConflict(axiosErrorWith(400, 'лимит компаний'))).toBe(false)
  })
})
