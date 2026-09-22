import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getProvidesServicesErrorMessage } from './providesServicesError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

describe('getProvidesServicesErrorMessage', () => {
  it('marks 409 as needing confirmation and surfaces the server text verbatim', () => {
    const err = axiosErrorWith(409, 'У специалиста 3 будущие записи. Они останутся в силе.')
    const result = getProvidesServicesErrorMessage(err)
    expect(result.needsConfirmation).toBe(true)
    expect(result.message).toBe('У специалиста 3 будущие записи. Они останутся в силе.')
  })

  it('gives a fallback message for 409 with no body', () => {
    const err = axiosErrorWith(409, '')
    const result = getProvidesServicesErrorMessage(err)
    expect(result.needsConfirmation).toBe(true)
    expect(result.message).toContain('будущие записи')
  })

  it('reports 403 as a permissions error without confirmation', () => {
    const result = getProvidesServicesErrorMessage(axiosErrorWith(403, ''))
    expect(result.needsConfirmation).toBe(false)
    expect(result.message).toContain('прав')
  })

  it('reports 404 as "not found"', () => {
    const result = getProvidesServicesErrorMessage(axiosErrorWith(404, ''))
    expect(result.needsConfirmation).toBe(false)
    expect(result.message).toContain('не найден')
  })

  it('falls back to a generic message for unexpected errors', () => {
    const result = getProvidesServicesErrorMessage(new Error('network down'))
    expect(result.needsConfirmation).toBe(false)
    expect(result.message).toBe('Не удалось сохранить изменение. Попробуйте снова.')
  })
})
