import type { AxiosError } from 'axios'

// ARCHITECTURE_CYCLE31.md §31.9.1 / API_CONTRACT_CYCLE31.md §31.26 — pure logic of the multi-file gallery upload:
// what is sent, what is dropped before sending, what does not fit. The server stays the only real validator
// (file signature, size, quota); these limits only save a doomed request.
export const PHOTO_MAX_PHOTOS = 10
export const PHOTO_MAX_BYTES = 5 * 1024 * 1024
export const PHOTO_ACCEPT = 'image/jpeg,image/png,image/webp'

const ACCEPTED_TYPES = PHOTO_ACCEPT.split(',')
const ACCEPTED_EXTENSIONS = /\.(jpe?g|png|webp)$/i

export type PhotoRejectReason = 'type' | 'size'

export interface PhotoBatchPlan {
  /** In selection order, at most `remainingSlots`. */
  accepted: File[]
  /** Dropped before sending — one status line per file. */
  rejected: { file: File; reason: PhotoRejectReason }[]
  /** Passed the checks but do not fit into the gallery — ONE message for all of them. */
  overLimit: File[]
}

function rejectReason(file: File): PhotoRejectReason | null {
  const typeOk = file.type === '' ? ACCEPTED_EXTENSIONS.test(file.name) : ACCEPTED_TYPES.includes(file.type)
  if (!typeOk) return 'type'
  if (file.size > PHOTO_MAX_BYTES) return 'size'
  return null
}

/** Type/size filtering first (rejected files take no slot), then truncation to the free slots. */
export function planPhotoBatch(files: readonly File[], remainingSlots: number): PhotoBatchPlan {
  const rejected: PhotoBatchPlan['rejected'] = []
  const passed: File[] = []
  for (const file of files) {
    const reason = rejectReason(file)
    if (reason) rejected.push({ file, reason })
    else passed.push(file)
  }
  const slots = Math.max(0, remainingSlots)
  return { accepted: passed.slice(0, slots), rejected, overLimit: passed.slice(slots) }
}

export function photoRejectText(reason: PhotoRejectReason): string {
  return reason === 'size' ? 'Файл больше 5 МБ' : 'Формат не поддерживается: нужен JPEG, PNG или WEBP'
}

export function overLimitText(files: readonly File[]): string {
  return `Не добавлено ${files.length} фото: в галерее не больше ${PHOTO_MAX_PHOTOS} фото: ${files.map((f) => f.name).join(', ')}`
}

/** API_CONTRACT_CYCLE31.md §31.26 п. 6 — worth a retry: 429, no response at all (network/timeout), 5xx. */
export function isTransientUploadError(err: unknown): boolean {
  const ax = err as AxiosError | undefined
  const status = ax?.response?.status
  if (status === undefined) return true
  return status === 429 || status >= 500
}
