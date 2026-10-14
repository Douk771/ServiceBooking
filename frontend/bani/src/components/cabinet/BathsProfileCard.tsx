import { CompanyProfileCard, type CompanyProfileSnapshot, type CompanyProfileTexts } from '@/components/company/CompanyProfileCard'
import { SlotNotice } from '@/components/slots/ui/SlotNotice'
import { getCompanyManageErrorMessage } from '@/utils/companyManageError'
import type { City } from '@/types'
import type { BathsCompanyManageDto } from '../../cabinet/types'

const TEXTS: CompanyProfileTexts = {
  title: 'Профиль комплекса',
  logoAlt: 'Логотип комплекса',
  nameRequired: 'Укажите название',
  phoneLabel: 'Телефон для гостей',
  emailLabel: 'Email для гостей',
  emailHint: 'Виден на странице комплекса',
  cityHint: 'Время сеансов и срок отмены считаются по часовому поясу комплекса. Гость видит пометку «время местное» с названием города',
  addressHint: 'Адрес виден гостям в каталоге и на странице комплекса',
  saveFailed: 'Не удалось сохранить профиль.',
  timeZoneChange: (from, to) =>
    `Часовой пояс комплекса изменится: ${from} → ${to}. Время уже оформленных броней не изменится, а расписание ресурсов будет читаться по новому поясу.`,
}

/** The generated manage DTO carries only the city's name: the region and the offset stay empty until the owner picks the city again. */
function cityOf(c: BathsCompanyManageDto): City | null {
  if (c.cityId == null || !c.cityName) return null
  return { id: c.cityId, name: c.cityName, region: '', timeZoneId: c.timeZoneId, utcOffsetMinutes: 0, label: c.cityName }
}

/**
 * «Профиль комплекса»: the shared `CompanyProfileCard` (name, description, contacts, address, map links, city and time zone, logo) over the
 * common `PUT /api/companies/{id}` (API_CONTRACT_CYCLE42.md §42.21). The city and the zone of a «Бани» company can be changed, the zone also by
 * hand. `BathPublicContactsNotice` (Т42-04) stands between the contacts and the address: the phone and the address become public.
 * Mount with `key={company.id}`: the form reads the company once.
 */
export function BathsProfileCard({ company, onSaved }: { company: BathsCompanyManageDto; onSaved: () => void }) {
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
    zone: { id: company.timeZoneId, offsetMinutes: null, isManual: company.timeZoneIsManual },
  }
  return (
    <CompanyProfileCard
      company={snapshot}
      texts={TEXTS}
      errorMessage={getCompanyManageErrorMessage}
      onChanged={onSaved}
      manualTimeZone
      contactsNotice={<SlotNotice textKey="BathPublicContactsNotice" />}
    />
  )
}
