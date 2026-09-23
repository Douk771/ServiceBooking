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
  it('400 "Слишком больш…" (byte-size limit) → file-size message', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'Слишком большой файл — максимум 5 МБ.'))).toBe(
      'Файл больше 5 МБ. Уменьшите изображение и попробуйте снова.',
    )
  })

  it('400 "Слишком больш…" (pixel-dimension limit) → same file-size message', () => {
    expect(
      getUploadErrorMessage(
        axiosErrorWith(400, 'Слишком большое изображение — попробуйте файл меньшего размера.'),
      ),
    ).toBe('Файл больше 5 МБ. Уменьшите изображение и попробуйте снова.')
  })

  it('400 "Можно загрузить JPEG…" → format message', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'Можно загрузить JPEG, PNG или WEBP.'))).toBe(
      'Поддерживаются только JPEG, PNG и WEBP.',
    )
  })

  it('400 "…не изображение" → format message (same branch as unsupported type)', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'Файл повреждён или это не изображение.'))).toBe(
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

  it('400 "закончилось место" → server disk message', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'На сервере закончилось место. Попробуйте позже.'))).toBe(
      'На сервере закончилось место. Попробуйте позже.',
    )
  })

  it('400 with an unrecognized body → generic Russian message, not the raw server text', () => {
    expect(getUploadErrorMessage(axiosErrorWith(400, 'Нужно выбрать файл для загрузки.'))).toBe(
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
