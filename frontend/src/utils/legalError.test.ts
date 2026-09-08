import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getLegalErrorMessage } from './legalError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

describe('getLegalErrorMessage', () => {
  it('503 → unavailable message', () => {
    expect(getLegalErrorMessage(axiosErrorWith(503, 'Правовые документы временно недоступны.'))).toBe(
      'Правовые документы временно недоступны.',
    )
  })

  it('503 with empty body → generic unavailable fallback', () => {
    expect(getLegalErrorMessage(axiosErrorWith(503, undefined))).toBe('Правовые документы временно недоступны.')
  })

  it('409 → re-read new revision message', () => {
    expect(
      getLegalErrorMessage(
        axiosErrorWith(409, 'Документы были обновлены ещё раз — перечитайте и примите новую редакцию.'),
      ),
    ).toBe('Документы были обновлены ещё раз — перечитайте и примите новую редакцию.')
  })

  it('404 → not found message', () => {
    expect(getLegalErrorMessage(axiosErrorWith(404, undefined))).toBe('Документ не найден.')
  })

  it('400 → required-fields message', () => {
    expect(getLegalErrorMessage(axiosErrorWith(400, 'Обе версии документов обязательны.'))).toBe(
      'Обе версии документов обязательны.',
    )
  })

  it('500 → generic fallback', () => {
    expect(getLegalErrorMessage(axiosErrorWith(500, undefined))).toBe(
      'Не удалось загрузить документ. Попробуйте снова.',
    )
  })
})
