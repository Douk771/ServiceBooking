import type { HouseAmenity, HouseObjectKind, HouseSetupInput, PricePeriodInput, StaysConflictCode } from '../types'
import { isHouseSlugValid, isHouseSlugReserved, HOUSE_SLUG_FORMAT_TEXT, HOUSE_SLUG_RESERVED_TEXT } from './slug'
import { diffDays, isIsoDate } from './stayDates'

/**
 * Client checks of the house forms (API_CONTRACT_CYCLE37.md §37.28). The texts are the server's; the server validates again and its 400
 * text is shown under the field it names. Money is whole rubles.
 */

export type Errors<K extends string> = Partial<Record<K, string>>

export type SetupField = 'name' | 'slug' | 'capacity' | 'extraBedsMax' | 'extraBedPriceRub'

export function validateSetup(s: HouseSetupInput): Errors<SetupField> {
  const e: Errors<SetupField> = {}
  const name = s.name.trim()
  if (!name || name.length > 100) e.name = 'Укажите название дома'
  if (!isHouseSlugValid(s.slug.trim())) e.slug = HOUSE_SLUG_FORMAT_TEXT
  else if (isHouseSlugReserved(s.slug.trim())) e.slug = HOUSE_SLUG_RESERVED_TEXT
  if (!Number.isInteger(s.capacity) || s.capacity < 1 || s.capacity > 50) e.capacity = 'Вместимость — от 1 до 50'
  if (s.extraBedsEnabled) {
    if (!Number.isInteger(s.extraBedsMax) || s.extraBedsMax < 1 || s.extraBedsMax > 10) e.extraBedsMax = 'Доп. мест — от 1 до 10'
    if (!Number.isInteger(s.extraBedPriceRub) || s.extraBedPriceRub < 0 || s.extraBedPriceRub > 100_000) e.extraBedPriceRub = 'Сумма — от 0 до 100 000 ₽'
  }
  return e
}

export function setupFieldOfError(text: string): SetupField | null {
  if (text.startsWith('Укажите название дома')) return 'name'
  if (text.startsWith('Адрес дома')) return 'slug'
  if (text.startsWith('Вместимость')) return 'capacity'
  if (text.startsWith('Доп. мест') || text.startsWith('Доп. места')) return 'extraBedsMax'
  if (text.startsWith('Сумма')) return 'extraBedPriceRub'
  return null
}

export type ContentField = 'description' | 'address' | 'checkInInfoText' | 'yandexMapsUrl' | 'twoGisUrl'

export function validateContent(c: { description: string; address: string; checkInInfoText: string }): Errors<ContentField> {
  const e: Errors<ContentField> = {}
  if (c.description.length > 4000) e.description = 'Описание — не длиннее 4000 символов'
  if (c.address.length > 500) e.address = 'Адрес — не длиннее 500 символов'
  if (c.checkInInfoText.length > 2000) e.checkInInfoText = 'Текст к заселению — не длиннее 2000 символов'
  return e
}

export const MAX_PRICE_RUB = 1_000_000

export function validatePrice(raw: number): string | null {
  return Number.isInteger(raw) && raw >= 1 && raw <= MAX_PRICE_RUB ? null : 'Цена — от 1 до 1 000 000 ₽'
}

export type PeriodField = 'startDate' | 'endDate' | 'priceRub'

export function validatePeriod(p: { startDate: string; endDate: string; priceRub: number }): Errors<PeriodField> {
  const e: Errors<PeriodField> = {}
  if (!isIsoDate(p.startDate)) e.startDate = 'Укажите дату начала'
  if (!isIsoDate(p.endDate)) e.endDate = 'Укажите дату окончания'
  if (!e.startDate && !e.endDate) {
    if (p.endDate < p.startDate) e.endDate = 'Дата окончания не может быть раньше начала'
    else if (diffDays(p.startDate, p.endDate) + 1 > 731) e.endDate = 'Период — не длиннее двух лет'
  }
  const price = validatePrice(p.priceRub)
  if (price) e.priceRub = price
  return e
}

export function toPeriodInput(p: { startDate: string; endDate: string; priceRub: number }): PricePeriodInput {
  return { startDate: p.startDate, endDate: p.endDate, priceRub: p.priceRub }
}

export type RegistryField = 'objectKind' | 'registryNumber' | 'registryUrl'

/** 5–32 characters: letters, digits, hyphen; the link is `https://…` up to 500 (API_CONTRACT_CYCLE37.md §37.28). */
export function validateRegistry(r: { objectKind: HouseObjectKind | ''; registryNumber: string; registryUrl: string }): Errors<RegistryField> {
  const e: Errors<RegistryField> = {}
  if (!r.objectKind) e.objectKind = 'Укажите вид объекта'
  const n = r.registryNumber.trim()
  if (n && !/^[\p{L}\p{N}-]{5,32}$/u.test(n)) e.registryNumber = 'Номер в реестре — 5–32 символа: буквы, цифры, дефис'
  const u = r.registryUrl.trim()
  if (u && (!/^https:\/\//i.test(u) || u.length > 500)) e.registryUrl = 'Ссылка на запись в реестре должна начинаться с https://'
  return e
}

export const OBJECT_KINDS: { value: HouseObjectKind; label: string; hint: string }[] = [
  { value: 'Residential', label: 'Жилое помещение', hint: 'Дом или квартира, не гостиница и не средство размещения' },
  { value: 'GuestHouse', label: 'Гостевой дом', hint: 'Входит в реестр средств размещения' },
  { value: 'OtherAccommodation', label: 'Иное средство размещения', hint: 'База отдыха, апарт-отель и т. п.' },
]

export const objectKindLabel = (k: HouseObjectKind): string => OBJECT_KINDS.find((o) => o.value === k)?.label ?? k

/** Why a house cannot be published yet, in the server's words (§37.28). */
const PUBLISH_PROBLEMS: Partial<Record<StaysConflictCode, string>> = {
  HouseArchived: 'Дом в архиве',
  NoPrice: 'Задайте цену: постоянную или хотя бы один период на будущие даты',
  ObjectKindRequired: 'Укажите вид объекта',
  RegistryNumberRequired: 'Для гостевого дома и средства размещения укажите номер в реестре',
  AttestationRequired: 'Подтвердите сведения о доме',
}

export function publishProblemText(code: StaysConflictCode): string {
  return PUBLISH_PROBLEMS[code] ?? code
}

/** A problem that is closed by the publish dialog itself (the attestation), so the list before it does not block the button. */
export const DIALOG_CLOSES: StaysConflictCode[] = ['AttestationRequired']

export const AMENITY_ORDER_FALLBACK: HouseAmenity[] = [
  'Wifi',
  'Kitchen',
  'Parking',
  'Sauna',
  'Bbq',
  'WashingMachine',
  'Tv',
  'Fireplace',
  'GearDryer',
  'SkiStorage',
  'LiftTransfer',
  'Dishwasher',
  'Terrace',
]
