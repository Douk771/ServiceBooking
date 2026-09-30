import { useQueryClient } from '@tanstack/react-query'
import { CompanyProfileCard, type CompanyProfileSnapshot, type CompanyProfileTexts, type TimeZoneLock } from '@/components/company/CompanyProfileCard'
import type { City } from '@/types'
import { getGoodsErrorMessage } from '../../utils/orderError'
import type { ShopManageDto } from '../../types'

function cityOf(shop: ShopManageDto): City | null {
  if (shop.cityId == null || !shop.cityName) return null
  return {
    id: shop.cityId,
    name: shop.cityName,
    region: shop.cityRegion ?? '',
    timeZoneId: shop.timeZoneId,
    utcOffsetMinutes: shop.utcOffsetMinutes ?? 0,
    label: shop.cityRegion ? `${shop.cityName}, ${shop.cityRegion}` : shop.cityName,
  }
}

const TEXTS: CompanyProfileTexts = {
  title: 'Профиль магазина',
  logoAlt: 'Логотип магазина',
  nameRequired: 'Введите название магазина',
  phoneLabel: 'Телефон для покупателей',
  emailLabel: 'Email для покупателей',
  emailHint: 'Виден на странице магазина',
  cityHint: 'Часы работы и время получения заказов считаются по часовому поясу города',
  addressHint: 'Адрес виден покупателям на странице магазина',
  saveFailed: 'Не удалось сохранить профиль.',
  timeZoneChange: (from, to) =>
    `Часовой пояс магазина изменится: ${from} → ${to}. Часы работы и время получения заказов будут считаться по новому поясу.`,
}

/**
 * ARCHITECTURE_CYCLE32.md §32.5.1 — the shop profile is the shared `CompanyProfileCard` (ARCHITECTURE_CYCLE26.md §552
 * chain, cycle-13 «mount with key={shop.id}» rule) plus shop-only glue: DTO mapping, the time-zone lock, cache refetch.
 */
export function ShopProfileSection({ shop }: { shop: ShopManageDto }) {
  const qc = useQueryClient()
  const company: CompanyProfileSnapshot = {
    id: shop.id,
    name: shop.name,
    description: shop.description ?? null,
    phone: shop.phone ?? null,
    email: shop.email ?? null,
    logoUrl: shop.logoUrl ?? null,
    address: shop.address ?? null,
    yandexMapsUrl: shop.yandexMapsUrl ?? null,
    twoGisUrl: shop.twoGisUrl ?? null,
    city: cityOf(shop),
    zone: { id: shop.timeZoneId, offsetMinutes: shop.utcOffsetMinutes ?? null, isManual: false },
  }
  const timeZoneLock: TimeZoneLock = {
    allowed: shop.timeZoneChangeAllowed,
    lockedText: shop.timeZoneChangeLockedText ?? null,
    fallbackText: 'У магазина уже есть заказы — часовой пояс сменить нельзя.',
  }
  const refetchShop = () => {
    void qc.invalidateQueries({ queryKey: ['shop', shop.id] })
    void qc.invalidateQueries({ queryKey: ['my-shops'] })
    void qc.invalidateQueries({ queryKey: ['storefront'] })
  }
  return (
    <CompanyProfileCard
      company={company}
      texts={TEXTS}
      errorMessage={getGoodsErrorMessage}
      onChanged={refetchShop}
      timeZoneLock={timeZoneLock}
    />
  )
}
