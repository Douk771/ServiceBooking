import { AxiosError } from 'axios'

/**
 * Maps failures from the company management screen (services, staff, commission, settings, logo) to
 * Russian messages.
 *
 * The reason this exists instead of showing `response.data` directly: the API's plain-text bodies are
 * English ("Slug already taken", "Employee limit reached for the current tariff plan."), and this
 * interface is Russian. Showing them verbatim put English sentences in front of salon owners.
 *
 * Matching is by status only. The bodies are deliberately not parsed for substrings — that is how the
 * older mappers ended up catching each other's errors.
 *
 * `fallback` names the action that failed, so a bare 500 or a network drop still says something
 * useful about what the user was doing.
 */
export function getCompanyManageErrorMessage(error: unknown, fallback: string): string {
  const ax = error as AxiosError
  switch (ax?.response?.status) {
    case 400:
      return 'Проверьте введённые данные — сервер их не принял.'
    case 402:
      return 'Достигнут лимит текущего тарифа. Смените тариф, чтобы продолжить.'
    case 403:
      // Forbid() sends an empty body, so without this the user would get the bare fallback. The usual
      // cause is losing access in another tab — being removed from the company, or a role change.
      return 'Недостаточно прав для этого действия. Возможно, ваш доступ к компании изменился.'
    case 404:
      return 'Объект не найден — возможно, его удалили в другой вкладке.'
    case 409:
      return 'Такое значение уже занято. Выберите другое.'
    default:
      return fallback
  }
}

/**
 * Logo upload answers 400 for several different reasons and the distinction is actionable — the owner
 * needs to know whether to shrink the file, convert it, or simply retry later. These are matched on
 * the server's own wording (the shared `ImageUploadService` pipeline behind
 * `CompaniesController.UploadLogo`), which is why this is separate from the status-only mapper above.
 *
 * All five rejection reasons the pipeline can produce are covered here, in the same buckets
 * `uploadError.ts` uses for the other four upload paths — otherwise "the server ran out of disk" or
 * "this file isn't an image" would fall through to the status-only 400 text ("Проверьте введённые
 * данные…"), which tells the owner nothing about what to do next.
 */
export function getLogoErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''

  if (body.includes('Слишком больш')) return 'Файл больше 5 МБ. Уменьшите изображение и попробуйте снова.'
  if (body.includes('Можно загрузить JPEG') || body.includes('не изображение'))
    return 'Неподдерживаемый формат. Подойдут JPEG, PNG или WEBP.'
  if (body.includes('Нужно выбрать файл')) return 'Файл не выбран.'
  if (body.includes('закончилось место')) return 'На сервере закончилось место. Попробуйте позже.'

  return getCompanyManageErrorMessage(error, 'Не удалось загрузить изображение.')
}
