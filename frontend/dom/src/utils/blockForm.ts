import type { HouseBlockInput, HouseBlockKind } from '../types'
import { addDays, nightsBetween } from './stayDates'

/**
 * The block form (`ManageBlocks`, API_CONTRACT_CYCLE37.md §37.29.2). The owner thinks in nights «с … по …» (the LAST night); the API
 * stores `endDate` as the check-out date (the first free date) — the conversion lives here, in one place, both ways.
 */
export const MAX_BLOCK_NIGHTS = 366
export const BLOCK_COMMENT_MAX = 300

export const BLOCK_KINDS: { value: HouseBlockKind; label: string }[] = [
  { value: 'Repair', label: 'Ремонт' },
  { value: 'Personal', label: 'Личное использование' },
  { value: 'Other', label: 'Другое' },
]

/** Last night → the API's `endDate`. */
export const toApiEnd = (lastNight: string): string => addDays(lastNight, 1)
/** The API's `endDate` → last night. */
export const toLastNight = (endDate: string): string => addDays(endDate, -1)

export type BlockField = 'dates' | 'comment' | 'houses'

export function validateBlock(
  f: { houseIds: string[]; firstNight: string; lastNight: string; comment: string },
  today: string,
  opts: { editing: boolean },
): Partial<Record<BlockField, string>> {
  const e: Partial<Record<BlockField, string>> = {}
  if (f.houseIds.length === 0) e.houses = 'Выберите хотя бы один дом'
  if (!f.firstNight || !f.lastNight) e.dates = 'Укажите даты'
  else if (f.lastNight < f.firstNight) e.dates = 'Последняя ночь не может быть раньше первой'
  else if (nightsBetween(f.firstNight, toApiEnd(f.lastNight)) > MAX_BLOCK_NIGHTS) e.dates = 'Блокировка — не длиннее 366 ночей'
  else if (!opts.editing && f.firstNight < today) e.dates = 'Нельзя блокировать прошедшие даты'
  if (f.comment.length > BLOCK_COMMENT_MAX) e.comment = 'Комментарий — не длиннее 300 символов'
  return e
}

export function toBlockInput(houseId: string, f: { firstNight: string; lastNight: string; kind: HouseBlockKind; comment: string }): HouseBlockInput {
  return { houseId, startDate: f.firstNight, endDate: toApiEnd(f.lastNight), kind: f.kind, comment: f.comment.trim() || null }
}

export interface MassResult {
  houseId: string
  ok: boolean
  error?: string
}

/**
 * Several houses = one request per house, in order (there is no bulk route). A failure of one house does not stop the rest; the
 * caller shows which houses are blocked and which are not, with the server's reason for each.
 */
export async function blockHouses(
  houseIds: string[],
  send: (houseId: string) => Promise<unknown>,
  explain: (err: unknown) => string,
): Promise<MassResult[]> {
  const out: MassResult[] = []
  for (const houseId of houseIds) {
    try {
      await send(houseId)
      out.push({ houseId, ok: true })
    } catch (err) {
      out.push({ houseId, ok: false, error: explain(err) })
    }
  }
  return out
}
