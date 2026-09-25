import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getClientRescheduleErrorMessage } from './clientRescheduleError'

function axiosError(status: number, data?: unknown): AxiosError {
  return { response: { status, data } } as AxiosError
}

describe('getClientRescheduleErrorMessage', () => {
  it('shows the server-composed 400 text verbatim (§287.2 — server already fills in the hour/day count)', () => {
    expect(getClientRescheduleErrorMessage(axiosError(400, 'Перенести запись можно не позже чем за 2 ч до визита'))).toBe(
      'Перенести запись можно не позже чем за 2 ч до визита',
    )
  })

  it('falls back to a generic message for a 400 with no body', () => {
    expect(getClientRescheduleErrorMessage(axiosError(400, ''))).toBe(
      'Перенести запись не получилось — проверьте выбранное время.',
    )
  })

  it('maps 402 to a Russian explanation', () => {
    expect(getClientRescheduleErrorMessage(axiosError(402, 'Online booking requires a paid subscription.'))).toBe(
      'Онлайн-запись сейчас недоступна: у салона нет активной подписки, разрешающей это.',
    )
  })

  it('maps 403 (AllowSelfBooking off) to a Russian explanation', () => {
    expect(getClientRescheduleErrorMessage(axiosError(403))).toBe('Салон сейчас не принимает переносы записей онлайн.')
  })

  it('maps 404 (own booking gone, or someone else\'s — §287.3) to the same not-found text', () => {
    expect(getClientRescheduleErrorMessage(axiosError(404))).toBe('Запись не найдена — возможно, её уже изменили.')
  })

  it('maps 409 (slot taken) to Russian', () => {
    expect(getClientRescheduleErrorMessage(axiosError(409, 'Time slot is no longer available'))).toBe(
      'Это время уже занято. Выберите другое.',
    )
  })

  it('falls back for an unrecognized status', () => {
    expect(getClientRescheduleErrorMessage(axiosError(500))).toBe('Не удалось перенести запись. Попробуйте снова.')
  })
})
