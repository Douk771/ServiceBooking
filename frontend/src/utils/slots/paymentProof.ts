/**
 * Payment proof files (ARCHITECTURE_CYCLE37.md §37.8, §37.14.5). For the guest this is «подтверждение оплаты (квитанция или скриншот
 * перевода)», never «чек» (Т37-06). The server decides by the file's bytes; these local checks only save a round trip.
 */
export const PROOF_ACCEPT = 'application/pdf,image/jpeg,image/png,image/webp'
export const PROOF_ALLOWED_TYPES = ['application/pdf', 'image/jpeg', 'image/png', 'image/webp']

export interface ProofRules {
  maxCount: number
  maxBytes: number
  acceptedTypes: string[]
}

export const DEFAULT_PROOF_RULES: ProofRules = { maxCount: 3, maxBytes: 10 * 1024 * 1024, acceptedTypes: PROOF_ALLOWED_TYPES }

/** Server wording (API_CONTRACT_CYCLE37.md §37.26.2) for a file that cannot be sent; null when it may go. */
export function proofFileProblem(file: { size: number; type: string }, rules: ProofRules): string | null {
  if (file.size <= 0) return 'Выберите файл'
  if (file.size > rules.maxBytes) return 'Файл больше 10 МБ'
  // An empty `type` happens for some phones; the server checks the signature anyway.
  if (file.type && !rules.acceptedTypes.includes(file.type)) return 'Можно приложить PDF, JPEG, PNG или WebP'
  return null
}

/** How many of the picked files fit into the free slots; the rest are named as skipped. */
export function splitBySlots<T>(files: T[], attached: number, maxCount: number): { take: T[]; skipped: number } {
  const free = Math.max(0, maxCount - attached)
  return { take: files.slice(0, free), skipped: Math.max(0, files.length - free) }
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} Б`
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} КБ`
  return `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} МБ`
}

export const isPdf = (contentType: string) => contentType === 'application/pdf'
