import type { ServiceManageDto, ServiceSetupInput, StaysServiceConflictCode } from '../types'
import { isServiceSlugValid, SERVICE_SLUG_FORMAT_TEXT } from './slug'

/** Forms of the service cabinet: client checks (the server repeats them; its 400 text is shown), request bodies, texts of the 409 codes. */

/** Moves an id one step up (-1) or down (+1); the same list when it cannot move. */
export function moveId(ids: readonly string[], id: string, delta: -1 | 1): string[] {
  const i = ids.indexOf(id)
  const j = i + delta
  if (i < 0 || j < 0 || j >= ids.length) return [...ids]
  const next = [...ids]
  ;[next[i], next[j]] = [next[j], next[i]]
  return next
}

/** What blocks the publication (`publishProblems[]`), in the server's words (§39.26). */
export const PUBLISH_PROBLEM_TEXT: Partial<Record<StaysServiceConflictCode, string>> = {
  ServiceArchived: 'Услуга в архиве',
  ServiceNoPrice: 'Добавьте хотя бы одно правило цены',
  ServiceNoWindows: 'Задайте свободное время: недельный шаблон или окна на даты в пределах горизонта',
}

export const NO_PREPAY_WARNING =
  'Без предоплаты время закрепляется за гостем сразу, а оплата — на месте. Гость может не прийти, и компания ничего не получит. Решите, готовы ли вы к этому.'

export interface SetupDraft {
  name: string
  slug: string
  minHours: number
  maxHours: number
  stepMinutes: 30 | 60
  bufferMinutes: number
  showBufferToGuests: boolean
  minLeadMinutes: number
  /** null — без предоплаты. */
  prepayPercent: number | null
  cancellationPolicy: ServiceSetupInput['cancellationPolicy']
  cancellationBoundaryHours: number
  availableForHouseBookings: boolean
}

export function setupDraftOf(s: ServiceManageDto): SetupDraft {
  return {
    name: s.name,
    slug: s.slug,
    minHours: s.minHours,
    maxHours: s.maxHours,
    stepMinutes: s.stepMinutes === 30 ? 30 : 60,
    bufferMinutes: s.bufferMinutes,
    showBufferToGuests: s.showBufferToGuests,
    minLeadMinutes: s.minLeadMinutes,
    prepayPercent: s.standalonePrepayPercent ?? null,
    cancellationPolicy: s.cancellationPolicy,
    cancellationBoundaryHours: s.cancellationBoundaryHours,
    availableForHouseBookings: s.availableForHouseBookings,
  }
}

export type SetupErrors = Partial<Record<keyof SetupDraft, string>>

/** Same checks and texts as the server's 400 (API_CONTRACT_CYCLE39.md §39.26). */
export function validateSetup(d: SetupDraft, range: { min: number; max: number }): SetupErrors {
  const e: SetupErrors = {}
  if (d.name.trim().length < 1 || d.name.trim().length > 100) e.name = 'Укажите название услуги'
  if (!isServiceSlugValid(d.slug)) e.slug = SERVICE_SLUG_FORMAT_TEXT
  if (!Number.isInteger(d.minHours) || d.minHours < 1 || d.minHours > 12) e.minHours = 'Минимум часов — от 1 до 12'
  if (!Number.isInteger(d.maxHours) || d.maxHours < d.minHours || d.maxHours > 12) e.maxHours = 'Максимум часов — от минимума до 12'
  if (d.bufferMinutes < 0 || d.bufferMinutes > 240 || d.bufferMinutes % 15 !== 0) e.bufferMinutes = 'Время на подготовку — от 0 до 240 минут с шагом 15'
  if (d.minLeadMinutes < 0 || d.minLeadMinutes > 2880 || d.minLeadMinutes % 30 !== 0) e.minLeadMinutes = 'Минимальное время до начала — от 0 до 48 часов с шагом 30 минут'
  if (d.prepayPercent != null && (!Number.isInteger(d.prepayPercent) || d.prepayPercent < 1 || d.prepayPercent > 100)) e.prepayPercent = 'Предоплата — от 1 до 100 % или без предоплаты'
  if (d.cancellationPolicy === 'PreparationCosts' && (d.cancellationBoundaryHours < range.min || d.cancellationBoundaryHours > range.max)) {
    e.cancellationBoundaryHours = `Срок для полного возврата — от ${range.min} до ${range.max} часов до начала`
  }
  return e
}

export function toSetupInput(d: SetupDraft): ServiceSetupInput {
  return {
    name: d.name.trim(),
    slug: d.slug.trim(),
    minHours: d.minHours,
    maxHours: d.maxHours,
    stepMinutes: d.stepMinutes,
    bufferMinutes: d.bufferMinutes,
    showBufferToGuests: d.showBufferToGuests,
    minLeadMinutes: d.minLeadMinutes,
    standalonePrepayPercent: d.prepayPercent,
    cancellationPolicy: d.cancellationPolicy,
    cancellationBoundaryHours: d.cancellationBoundaryHours,
    availableForHouseBookings: d.availableForHouseBookings,
  }
}

/** Which field a 400 text of `PUT …/setup` belongs to. */
export function setupFieldOfError(text: string): keyof SetupDraft | null {
  if (text.startsWith('Укажите название') || text.startsWith('Название')) return 'name'
  if (text.startsWith('Адрес услуги')) return 'slug'
  if (text.startsWith('Минимум часов')) return 'minHours'
  if (text.startsWith('Максимум часов')) return 'maxHours'
  if (text.startsWith('Шаг старта')) return 'stepMinutes'
  if (text.startsWith('Время на подготовку')) return 'bufferMinutes'
  if (text.startsWith('Минимальное время')) return 'minLeadMinutes'
  if (text.startsWith('Предоплата')) return 'prepayPercent'
  if (text.startsWith('Срок для полного возврата')) return 'cancellationBoundaryHours'
  return null
}

/** Is the saved state different from the draft (the save button lives on it). */
export function isSetupDirty(a: SetupDraft, b: SetupDraft): boolean {
  return JSON.stringify(a) !== JSON.stringify(b)
}
