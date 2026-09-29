import type { CatalogConflictCode } from '../types'
import { getGoodsErrorMessage, readConflict } from './orderError'

/** Cabinet errors: catalog/address 409s are JSON with a ready Russian `message` (§406.2). */
export function getCatalogErrorMessage(error: unknown, fallback?: string): string {
  return getGoodsErrorMessage(error, fallback)
}

export function catalogConflictCode(error: unknown): CatalogConflictCode | null {
  return readConflict<{ code: CatalogConflictCode; message: string }>(error)?.code ?? null
}
