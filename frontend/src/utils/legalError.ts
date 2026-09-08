import { AxiosError } from 'axios'

/**
 * Maps failures of the legal-document/consent endpoints (GET /api/legal/documents/{type},
 * POST /api/legal/accept) to a clear Russian message. All bodies are plain text
 * (API_CONTRACT.md §0.2, §1, §2, §4).
 */
export function getLegalErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''

  switch (status) {
    case 503:
      return body || 'Правовые документы временно недоступны.'
    case 409:
      // The document was replaced again while the user was reading it (API_CONTRACT.md §4).
      return body || 'Документы были обновлены ещё раз — перечитайте и примите новую редакцию.'
    case 404:
      return 'Документ не найден.'
    case 400:
      return body || 'Обе версии документов обязательны.'
    default:
      return body || 'Не удалось загрузить документ. Попробуйте снова.'
  }
}
