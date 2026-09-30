import { useQueryClient } from '@tanstack/react-query'
import {
  CompanyProfileCard,
  type CompanyProfileSnapshot,
  type CompanyProfileTexts,
} from '../../components/company/CompanyProfileCard'
import { getCompanyManageErrorMessage } from '../../utils/companyManageError'
import type { City, Company } from '../../types'

const TEXTS: CompanyProfileTexts = {
  title: 'Профиль салона',
  logoAlt: 'Логотип салона',
  nameRequired: 'Введите название салона',
  phoneLabel: 'Телефон для клиентов',
  emailLabel: 'Email для клиентов',
  emailHint: 'Виден на странице салона',
  cityHint: 'Напоминания клиентам и срок переноса и отмены записи считаются по часовому поясу салона',
  addressHint: 'Адрес виден клиентам на странице салона',
  saveFailed: 'Не удалось сохранить профиль салона.',
  timeZoneChange: (from, to) =>
    `Часовой пояс салона изменится: ${from} → ${to}. Срок переноса и отмены сразу начнёт считаться по новому поясу, напоминания — для записей, созданных или перенесённых после смены. Время уже созданных записей и рабочие часы не изменятся.`,
}

function cityOf(c: Company): City | null {
  if (c.cityId == null || !c.cityName) return null
  return {
    id: c.cityId,
    name: c.cityName,
    region: c.cityRegion ?? '',
    timeZoneId: c.timeZoneId ?? '',
    utcOffsetMinutes: c.utcOffsetMinutes ?? 0,
    label: c.cityRegion ? `${c.cityName}, ${c.cityRegion}` : c.cityName,
  }
}

/**
 * ARCHITECTURE_CYCLE32.md §32.5.2 — the salon profile: the shared `CompanyProfileCard` with the salon-only flags
 * (manual time zone, legacy-phone hint). Mount with `key={company.id}` (cycle-13 rule, §32.4.4).
 */
export function SalonProfileSection({ company }: { company: Company }) {
  const qc = useQueryClient()
  const snapshot: CompanyProfileSnapshot = {
    id: company.id,
    name: company.name,
    description: company.description ?? null,
    phone: company.phone ?? null,
    email: company.email ?? null,
    logoUrl: company.logoUrl ?? null,
    address: company.address ?? null,
    yandexMapsUrl: company.yandexMapsUrl ?? null,
    twoGisUrl: company.twoGisUrl ?? null,
    city: cityOf(company),
    zone: {
      id: company.timeZoneId ?? null,
      offsetMinutes: company.utcOffsetMinutes ?? null,
      isManual: !!company.timeZoneIsManual,
    },
  }
  const onChanged = () => {
    void qc.invalidateQueries({ queryKey: ['my-companies'] })
    void qc.invalidateQueries({ queryKey: ['company'] })
  }
  return (
    <CompanyProfileCard
      company={snapshot}
      texts={TEXTS}
      errorMessage={getCompanyManageErrorMessage}
      onChanged={onChanged}
      manualTimeZone
      legacyPhoneHint
    />
  )
}
