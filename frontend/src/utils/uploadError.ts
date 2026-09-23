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
    // API_CONTRACT_CYCLE10.md §127.1 — the one cycle-10 upload error, already Russian at the source
    // (it's the only text unique to this endpoint), so it's matched by the exact server string rather
    // than translated — no second error dictionary (ARCHITECTURE_CYCLE10.md §109.2).
    if (body.includes('В галерее салона может быть не больше 10 фотографий')) return body
    // ImageUploadService now answers in Russian directly (US-125); matched by the shared "Слишком
    // больш…" prefix the server deliberately uses for both the byte-size and the pixel-dimension
    // rejection, so both land in this one bucket.
    if (body.includes('Слишком больш')) return 'Файл больше 5 МБ. Уменьшите изображение и попробуйте снова.'
    if (body.includes('Можно загрузить JPEG') || body.includes('не изображение'))
      return 'Поддерживаются только JPEG, PNG и WEBP.'
    if (body.includes('закончилось место')) return 'На сервере закончилось место. Попробуйте позже.'
    // Any other 400 (e.g. "Нужно выбрать файл для загрузки.") isn't in the mapped set — show a
    // generic Russian message rather than the server's raw text (API_CONTRACT.md §6).
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
