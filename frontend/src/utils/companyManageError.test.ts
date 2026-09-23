import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getLogoErrorMessage } from './companyManageError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

// Regression coverage for a review finding: the logo-upload path uses its own substring mapper
// (separate from `uploadError.ts`), and it fell out of sync with the server's `ImageUploadService`
// pipeline — two of the six rejection reasons ("ran out of disk" and "file is corrupted / not an
// image") fell through to the generic status-only 400 text instead of a message that tells the owner
// what to do next. This test pins all six reasons the shared pipeline can produce.
describe('getLogoErrorMessage', () => {
  it('"Нужно выбрать файл…" → file-not-selected message', () => {
    expect(getLogoErrorMessage(axiosErrorWith(400, 'Нужно выбрать файл для загрузки.'))).toBe(
      'Файл не выбран.',
    )
  })

  it('"Слишком большой файл…" (byte-size limit) → file-size message', () => {
    expect(getLogoErrorMessage(axiosErrorWith(400, 'Слишком большой файл — максимум 5 МБ.'))).toBe(
      'Файл больше 5 МБ. Уменьшите изображение и попробуйте снова.',
    )
  })

  it('"Слишком большое изображение…" (pixel-dimension limit) → same file-size message', () => {
    expect(
      getLogoErrorMessage(
        axiosErrorWith(400, 'Слишком большое изображение — попробуйте файл меньшего размера.'),
      ),
    ).toBe('Файл больше 5 МБ. Уменьшите изображение и попробуйте снова.')
  })

  it('"Можно загрузить JPEG…" → unsupported-format message', () => {
    expect(getLogoErrorMessage(axiosErrorWith(400, 'Можно загрузить JPEG, PNG или WEBP.'))).toBe(
      'Неподдерживаемый формат. Подойдут JPEG, PNG или WEBP.',
    )
  })

  it('"…это не изображение" (corrupted file) → unsupported-format message, not the generic 400 fallback', () => {
    expect(getLogoErrorMessage(axiosErrorWith(400, 'Файл повреждён или это не изображение.'))).toBe(
      'Неподдерживаемый формат. Подойдут JPEG, PNG или WEBP.',
    )
  })

  it('"…закончилось место" (disk full) → server-out-of-space message, not the generic 400 fallback', () => {
    expect(
      getLogoErrorMessage(axiosErrorWith(400, 'На сервере закончилось место. Попробуйте позже.')),
    ).toBe('На сервере закончилось место. Попробуйте позже.')
  })

  it('unrecognized 400 body → generic status fallback', () => {
    expect(getLogoErrorMessage(axiosErrorWith(400, 'Something else entirely.'))).toBe(
      'Проверьте введённые данные — сервер их не принял.',
    )
  })

  it('non-400 status (e.g. network drop) → upload-specific fallback', () => {
    expect(getLogoErrorMessage(new AxiosError('Network Error'))).toBe('Не удалось загрузить изображение.')
  })
})
