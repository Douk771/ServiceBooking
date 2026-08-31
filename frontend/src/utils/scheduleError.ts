import type { AxiosError } from 'axios'

// Schedule endpoints answer with a plain-text body (see API_CONTRACT.md §6, §7), so the server text is
// already user-facing and is preferred whenever present. 403 is the exception: it is most commonly hit
// when a master was removed from the company but the tab is still open, and the server body there is
// empty (Forbid() writes no content) — so it needs its own explicit text rather than a blank message.
export function getScheduleErrorMessage(err: unknown): string {
  const ax = err as AxiosError
  const serverMsg = typeof ax?.response?.data === 'string' ? ax.response.data : ''
  if (ax?.response?.status === 403) return 'Недостаточно прав для изменения этого расписания.'
  return serverMsg || 'Не удалось сохранить расписание. Попробуйте снова.'
}
