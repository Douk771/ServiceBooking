import type { StayProviderStatus } from './types'

/**
 * Client checks of «Об исполнителе» (API_CONTRACT_CYCLE37.md §37.27.5, as in «Дома»; ЮР-3). The server has the last word and prints its own
 * 400 text under the field it names — these checks only save a round trip.
 */

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

/** INN length by status: 10 digits for an organization, 12 for the others; OGRN: 13 / 15 / none. */
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
