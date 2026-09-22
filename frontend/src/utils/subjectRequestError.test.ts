import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getSubjectRequestErrorMessage, getSubjectRequestAdminErrorMessage } from './subjectRequestError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

describe('getSubjectRequestErrorMessage', () => {
  it('429 → rate-limit message', () => {
    expect(getSubjectRequestErrorMessage(axiosErrorWith(429, 'Слишком много обращений.'))).toBe(
      'Слишком много обращений.',
    )
  })

  it('429 with empty body → generic fallback', () => {
    expect(getSubjectRequestErrorMessage(axiosErrorWith(429, undefined))).toBe(
      'Слишком много обращений с этого адреса. Попробуйте позже.',
    )
  })

  it('400 → server text as-is', () => {
    expect(getSubjectRequestErrorMessage(axiosErrorWith(400, 'Капча не пройдена.'))).toBe('Капча не пройдена.')
  })

  it('500 → generic fallback, not the document-specific one from getLegalErrorMessage', () => {
    expect(getSubjectRequestErrorMessage(axiosErrorWith(500, undefined))).toBe(
      'Не удалось отправить обращение. Попробуйте снова.',
    )
  })
})

describe('getSubjectRequestAdminErrorMessage', () => {
  it('400 → server text (missing resolution)', () => {
    expect(getSubjectRequestAdminErrorMessage(axiosErrorWith(400, 'Укажите результат.'))).toBe('Укажите результат.')
  })

  it('403 → permissions message', () => {
    expect(getSubjectRequestAdminErrorMessage(axiosErrorWith(403, undefined))).toBe(
      'Недостаточно прав для этого действия.',
    )
  })

  it('404 → not-found message specific to a request, not a legal document', () => {
    expect(getSubjectRequestAdminErrorMessage(axiosErrorWith(404, undefined))).toBe(
      'Обращение не найдено — возможно, обновите список.',
    )
  })

  it('500 → generic fallback', () => {
    expect(getSubjectRequestAdminErrorMessage(axiosErrorWith(500, undefined))).toBe('Не удалось сохранить. Попробуйте снова.')
  })
})
