import { AxiosError } from 'axios'

/**
 * `PUT /api/admin/companies/{id}/owner` — API_CONTRACT_CYCLE20.md §437.3 (US-20-07, LG6). Both 400
 * ("User not found"/"User account has been deleted", existing) and 409 (NEW this cycle: the new
 * owner isn't linked to the company's billing account — "он должен быть держателем аккаунта или
 * сотрудником одной из его компаний") are bare server strings that must be shown VERBATIM (§441 item
 * 12 names this rule for the sibling transfer endpoint; the same "server composes the sentence"
 * convention applies here — there's nothing for the frontend to add). Before this cycle the caller
 * showed a hardcoded "Не удалось сменить владельца" for every failure, silently dropping even the
 * pre-existing 400 text — fixed here rather than left in place, since the NEW 409 text is the one
 * thing that actually tells the operator what to do next (pick someone from the target account).
 */
export function getChangeOwnerErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''

  switch (status) {
    case 400:
    case 409:
      return body || 'Не удалось сменить владельца.'
    case 404:
      return 'Компания не найдена — возможно, её уже удалили.'
    case 403:
      return 'Недостаточно прав для этого действия.'
    default:
      return 'Не удалось сменить владельца. Попробуйте снова.'
  }
}
