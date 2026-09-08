import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getUploadErrorMessage } from './uploadError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

describe('getUploadErrorMessage', () => {
  it('400 "too large" → file-size message', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'Image is too large — the limit is 5 MB'))).toBe(
      'Файл больше 5 МБ. Уменьшите изображение и попробуйте снова.',
    )
  })

  it('400 "Unsupported image type" → format message', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'Unsupported image type — use JPEG, PNG or WEBP'))).toBe(
      'Поддерживаются только JPEG, PNG и WEBP.',
    )
  })

  it('400 "not a valid image" → format message (same branch as unsupported type)', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'File is not a valid image'))).toBe(
      'Поддерживаются только JPEG, PNG и WEBP.',
    )
  })

  it('400 "5 photos" → per-note photo limit message', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'This note already has 5 photos.'))).toBe(
      'К одной заметке можно приложить не больше 5 фото.',
    )
  })

  it('400 quota with numbers → quota message including both numbers', () => {
    expect(
      getUploadErrorMessage(
        axiosErrorWith(400, 'Photo storage quota exceeded: 98 of 100 MB used. Upgrade the plan for more space.'),
      ),
    ).toBe('Место под фото закончилось: занято 98 из 100 МБ. Смените тариф, чтобы загрузить больше.')
  })

  it('400 quota without numbers → generic quota message', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'Photo storage quota exceeded.'))).toBe(
      'Место под фото закончилось. Смените тариф, чтобы загрузить больше.',
    )
  })

  it('400 "storage is full" → server disk message', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'Server storage is full — try again later.'))).toBe(
      'На сервере закончилось место. Попробуйте позже.',
    )
  })

  it('400 with an unrecognized body → generic Russian message, not the raw server text', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'File is required'))).toBe(
      'Не удалось загрузить фото. Проверьте файл и попробуйте снова.',
    )
  })

  it('429 → rate limit message regardless of body', () => {
    expect(getUploadErrorMessage(axiosErrorWith(429, 'Too many uploads. Try again in a minute.'))).toBe(
      'Слишком много загрузок подряд. Подождите минуту.',
    )
  })

  it('413 → same as file-too-large', () => {
    expect(getUploadErrorMessage(axiosErrorWith(413, undefined))).toBe(
      'Файл больше 5 МБ. Уменьшите изображение и попробуйте снова.',
    )
  })

  it('403 → insufficient rights message', () => {
    expect(getUploadErrorMessage(axiosErrorWith(403, undefined))).toBe('Недостаточно прав для этого действия.')
  })

  it('404 → not found message', () => {
    expect(getUploadErrorMessage(axiosErrorWith(404, undefined))).toBe('Фото или заметка не найдены.')
  })

  it('500 → generic fallback', () => {
    expect(getUploadErrorMessage(axiosErrorWith(500, undefined))).toBe('Не удалось загрузить фото. Попробуйте снова.')
  })

  it('no response at all (network error) → generic fallback', () => {
    expect(getUploadErrorMessage(new Error('Network Error'))).toBe('Не удалось загрузить фото. Попробуйте снова.')
  })
})
