import { describe, it, expect } from 'vitest'
import { getChangeOwnerErrorMessage } from './companyOwnerError'

function axiosError(status: number, data: unknown) {
  return { isAxiosError: true, response: { status, data } }
}

describe('getChangeOwnerErrorMessage', () => {
  it('shows the new 409 "not linked to the account" text verbatim (US-20-07, LG6)', () => {
    const text = 'Новый ответственный не связан с биллинг-аккаунтом компании: он должен быть держателем аккаунта или сотрудником одной из его компаний.'
    expect(getChangeOwnerErrorMessage(axiosError(409, text))).toBe(text)
  })

  it('shows the existing 400 text verbatim (e.g. "User not found")', () => {
    expect(getChangeOwnerErrorMessage(axiosError(400, 'User not found'))).toBe('User not found')
  })

  it('falls back to a generic message when the body is empty', () => {
    expect(getChangeOwnerErrorMessage(axiosError(409, ''))).toBe('Не удалось сменить владельца.')
  })

  it('maps 404/403 to fixed Russian messages', () => {
    expect(getChangeOwnerErrorMessage(axiosError(404, ''))).toMatch(/не найдена/)
    expect(getChangeOwnerErrorMessage(axiosError(403, ''))).toMatch(/Недостаточно прав/)
  })
})
