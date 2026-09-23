import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getPushErrorMessage } from './pushError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

describe('getPushErrorMessage', () => {
  it('400 → server text verbatim', () => {
    expect(getPushErrorMessage(axiosErrorWith(400, 'endpoint пуст'))).toBe('endpoint пуст')
  })

  it('409 → server text, or the standard "subsystem disabled" fallback', () => {
    expect(getPushErrorMessage(axiosErrorWith(409, 'Уведомления на устройство сейчас недоступны'))).toBe(
      'Уведомления на устройство сейчас недоступны',
    )
    expect(getPushErrorMessage(axiosErrorWith(409, ''))).toBe('Уведомления на устройство сейчас недоступны.')
  })

  it('429 → server text or rate-limit fallback', () => {
    expect(getPushErrorMessage(axiosErrorWith(429, ''))).toBe('Слишком много попыток. Повторите позже.')
  })

  it('403 → generic permissions message (body is empty per §116)', () => {
    expect(getPushErrorMessage(axiosErrorWith(403, ''))).toBe('Недостаточно прав для этого действия.')
  })

  it('404 → generic "already gone" message (chuzhaya podpiska never confirms existence)', () => {
    expect(getPushErrorMessage(axiosErrorWith(404, ''))).toBe('Устройство уже отключено или не найдено.')
  })

  it('unknown status → fallback', () => {
    expect(getPushErrorMessage(axiosErrorWith(500, ''))).toBe('Не удалось выполнить действие. Попробуйте снова.')
  })
})
