import type { StayCancellationPolicy, StayProviderStatus, StaysSettingsDto } from '../types'

/**
 * Client checks of the company settings form (API_CONTRACT_CYCLE37.md §37.27.3–§37.27.5). The texts are the server's; the checks only
 * save a round trip — the server validates again and its 400 text is shown under the field it names.
 */

export const HALF_HOURS: string[] = Array.from({ length: 48 }, (_, i) => `${String(Math.floor(i / 2)).padStart(2, '0')}:${i % 2 ? '30' : '00'}`)

const toMinutes = (t: string): number => {
  const [h, m] = t.split(':').map(Number)
  return h * 60 + m
}

export type SettingsErrors = Partial<Record<keyof StaysSettingsDto, string>>

export function validateSettings(s: StaysSettingsDto): SettingsErrors {
  const e: SettingsErrors = {}
  if (!HALF_HOURS.includes(s.checkInTime) || !HALF_HOURS.includes(s.checkOutTime)) e.checkInTime = 'Время — с шагом 30 минут'
  else if (toMinutes(s.checkOutTime) > toMinutes(s.checkInTime)) e.checkOutTime = 'Время выезда не может быть позже времени заезда'
  if (!Number.isInteger(s.minNights) || s.minNights < 1 || s.minNights > 30) e.minNights = 'Минимум ночей — от 1 до 30'
  if (!Number.isInteger(s.maxNights) || s.maxNights < 1 || s.maxNights > 90) e.maxNights = 'Максимум ночей — от 1 до 90'
  if (!e.minNights && !e.maxNights && s.minNights > s.maxNights) e.minNights = 'Минимум не может быть больше максимума'
  if (!Number.isInteger(s.horizonDays) || s.horizonDays < 30 || s.horizonDays > 730) e.horizonDays = 'Горизонт бронирования — от 30 до 730 дней'
  if (!Number.isInteger(s.holdMinutes) || s.holdMinutes < 10 || s.holdMinutes > 180) e.holdMinutes = 'Время на оплату — от 10 до 180 минут'
  if (!Number.isInteger(s.prepayPercent) || s.prepayPercent < 0 || s.prepayPercent > 100) e.prepayPercent = 'Предоплата — от 0 до 100 %'
  if (!Number.isInteger(s.dogFeeRub) || s.dogFeeRub < 0 || s.dogFeeRub > 100_000) e.dogFeeRub = 'Сумма — от 0 до 100 000 ₽'
  if (!Number.isInteger(s.cotFeeRub) || s.cotFeeRub < 0 || s.cotFeeRub > 100_000) e.cotFeeRub = 'Сумма — от 0 до 100 000 ₽'
  if (!HALF_HOURS.includes(s.checkInInfoSendTime)) e.checkInInfoSendTime = 'Время — с шагом 30 минут'
  if ((s.checkInInfoText ?? '').length > 2000) e.checkInInfoText = 'Текст к заселению — не длиннее 2000 символов'
  return e
}

/** Which field a 400 text of `PUT …/settings` belongs to (the server says it in words, §37.27.3). */
export function settingsFieldOfError(text: string): keyof StaysSettingsDto | null {
  if (text.startsWith('Время выезда')) return 'checkOutTime'
  if (text.startsWith('Время — с шагом')) return 'checkInTime'
  if (text.startsWith('Минимум ночей') || text.startsWith('Минимум не может')) return 'minNights'
  if (text.startsWith('Максимум ночей')) return 'maxNights'
  if (text.startsWith('Горизонт')) return 'horizonDays'
  if (text.startsWith('Время на оплату')) return 'holdMinutes'
  if (text.startsWith('Предоплата')) return 'prepayPercent'
  if (text.startsWith('Сумма')) return 'dogFeeRub'
  if (text.startsWith('Текст к заселению')) return 'checkInInfoText'
  return null
}

/** The three cancellation templates the owner can pick (ЮР-1) — never «Мягкий/Средний/Строгий», never a percentage of the stay. */
export const CANCELLATION_TEMPLATES: { value: StayCancellationPolicy; title: string; text: string }[] = [
  {
    value: 'Standard',
    title: 'Стандартный',
    text: 'Отмена до 00:00 дня заезда — возврат предоплаты полностью. В день заезда, при опоздании или незаезде вы вправе удержать не больше стоимости первой ночи, остальное возвращается.',
  },
  {
    value: 'Flexible',
    title: 'Гибкий',
    text: 'Отмена до времени заезда — возврат полностью. Позже или при незаезде — удержание не больше стоимости первой ночи.',
  },
  {
    value: 'NoDeductions',
    title: 'Без удержаний',
    text: 'При отмене до времени заезда предоплата возвращается полностью.',
  },
]

export const PREPAY_ZERO_WARNING =
  'При предоплате 0 % гости смогут бронировать без оплаты: бронь сразу «Подтверждена», а защита — только капча и лимиты.'

export const PREPAY_HINT = 'Предоплата больше стоимости первой ночи не защищает вас сильнее: при отмене до дня заезда возвращается вся.'

export const PROVIDER_STATUSES: { value: StayProviderStatus; label: string }[] = [
  { value: 'Organization', label: 'Организация' },
  { value: 'IndividualEntrepreneur', label: 'Индивидуальный предприниматель' },
  { value: 'SelfEmployed', label: 'Плательщик налога на профессиональный доход (самозанятый)' },
  { value: 'Individual', label: 'Физическое лицо' },
]

export interface ProviderForm {
  status: StayProviderStatus | ''
  name: string
  inn: string
  ogrn: string
  claimsAddress: string
}

export type ProviderErrors = Partial<Record<keyof ProviderForm, string>>

/** INN length by status: 10 digits for an organization, 12 for the others; OGRN: 13 / 15 / none (§37.27.5). */
export function innLengthFor(status: StayProviderStatus): number {
  return status === 'Organization' ? 10 : 12
}

export function ogrnLengthFor(status: StayProviderStatus): number | null {
  if (status === 'Organization') return 13
  if (status === 'IndividualEntrepreneur') return 15
  return null
}

export function validateProvider(f: ProviderForm): ProviderErrors {
  const e: ProviderErrors = {}
  if (!f.status) {
    e.status = 'Выберите статус исполнителя'
    return e
  }
  const name = f.name.trim()
  if (!name) e.name = f.status === 'Organization' ? 'Укажите наименование' : 'Укажите ФИО или наименование'
  else if (name.length > 300) e.name = 'Не длиннее 300 символов'
  const inn = f.inn.replace(/\D/g, '')
  if (inn.length !== innLengthFor(f.status) || /\D/.test(f.inn.trim())) e.inn = 'Неверный ИНН'
  const need = ogrnLengthFor(f.status)
  const ogrn = f.ogrn.replace(/\D/g, '')
  if (need) {
    if (!ogrn) e.ogrn = need === 13 ? 'Укажите ОГРН' : 'Укажите ОГРНИП'
    else if (ogrn.length !== need) e.ogrn = 'Неверный ОГРН'
  }
  const claims = f.claimsAddress.trim()
  if (!claims) e.claimsAddress = 'Укажите адрес для претензий'
  else if (claims.length > 500) e.claimsAddress = 'Не длиннее 500 символов'
  return e
}

export function providerFieldOfError(text: string): keyof ProviderForm | null {
  if (/ИНН/.test(text)) return 'inn'
  if (/ОГРН/.test(text)) return 'ogrn'
  if (/адрес для претензий/i.test(text)) return 'claimsAddress'
  return null
}
