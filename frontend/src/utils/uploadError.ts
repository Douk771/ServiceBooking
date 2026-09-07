import { AxiosError } from 'axios'

/**
 * Maps a failed photo upload/delete request (client-note photos, avatar, service image, logo) to a
 * clear, actionable Russian message.
 *
 * Bodies are read as plain strings (API_CONTRACT.md §0.2 — new 400s stay `text/plain`) and matched by
 * substring, because the same status code covers several distinct causes here (file too big vs wrong
 * format vs quota vs photo-count limit) and the fix is different for each. `429` has no substring to
 * match on — the rate limiter's body is fixed text — so it's matched by status alone.
 */
export function getUploadErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''

  if (status === 400) {
    if (body.includes('quota')) {
      // "Photo storage quota exceeded: 98 of 100 MB used. Upgrade the plan for more space."
      const match = body.match(/(\d+(?:\.\d+)?)\s*of\s*(\d+(?:\.\d+)?)\s*MB/i)
      return match
        ? `Место под фото закончилось: занято ${match[1]} из ${match[2]} МБ. Смените тариф, чтобы загрузить больше.`
        : 'Место под фото закончилось. Смените тариф, чтобы загрузить больше.'
    }
    if (body.includes('5 photos')) return 'К одной заметке можно приложить не больше 5 фото.'
    if (body.includes('too large')) return 'Файл больше 5 МБ. Уменьшите изображение и попробуйте снова.'
    if (body.includes('Unsupported image type') || body.includes('not a valid image'))
      return 'Поддерживаются только JPEG, PNG и WEBP.'
    if (body.includes('storage is full')) return 'На сервере закончилось место. Попробуйте позже.'
    // Any other 400 (e.g. "File is required") isn't in the mapped set — show a generic Russian
    // message rather than the server's raw (often English) text (API_CONTRACT.md §6).
    return 'Не удалось загрузить фото. Проверьте файл и попробуйте снова.'
  }

  switch (status) {
    case 413:
      return 'Файл больше 5 МБ. Уменьшите изображение и попробуйте снова.'
    case 429:
      return 'Слишком много загрузок подряд. Подождите минуту.'
    case 403:
      return 'Недостаточно прав для этого действия.'
    case 404:
      return 'Фото или заметка не найдены.'
    default:
      return 'Не удалось загрузить фото. Попробуйте снова.'
  }
}
