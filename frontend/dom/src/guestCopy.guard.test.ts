// @vitest-environment node
import { describe, it, expect } from 'vitest'

// QA цикл 39 (ЮР39-8, Т39-03, Т39-04, Т39-14, L39-9): в исходниках ГОСТЕВЫХ экранов и общих форматов времени нет слов, которых гость видеть не должен.
// Комментарии вырезаются (там слова объясняют запрет); проверяется всё остальное, включая строковые литералы. Кабинетные (cabinet/, staff/) файлы не сканируются:
// персоналу «бизнес-день» и формат «(след. дня)» разрешены.
const raw = import.meta.glob(
  [
    './pages/*.tsx',
    './components/services/*.tsx',
    './components/*.tsx',
    './utils/*.ts',
  ],
  { query: '?raw', import: 'default', eager: true },
) as Record<string, string>

const STAFF_ONLY = /(cabinet|staff|Board|Schedule|Reminder|reminder|ServiceDay|serviceDay|serviceForms|serviceDay|blockForm|manualBooking|houseForms|permissions|bookingFilters|boardLayout|priceCalendar|windows|Windows)/
const GUEST = Object.entries(raw).filter(([p]) => !/\.test\.tsx?$/.test(p) && !STAFF_ONLY.test(p))

function stripComments(src: string): string {
  return src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"`])\/\/.*$/gm, '$1')
}

describe('guest-facing sources of dom (QA цикл 39)', () => {
  it('scans a meaningful set of files', () => {
    expect(GUEST.length).toBeGreaterThan(15)
    const names = GUEST.map(([p]) => p)
    expect(names.some((n) => n.endsWith('ServicePage.tsx'))).toBe(true)
    expect(names.some((n) => n.endsWith('ServiceOrderPage.tsx'))).toBe(true)
    expect(names.some((n) => n.endsWith('ServicePickDialog.tsx'))).toBe(true)
  })

  it.each([
    ['«бизнес-день»', /бизнес-?\s*д[её]н/i],
    ['«часы 6…30»', /часы\s*6\s*(…|\.\.\.|–|-)\s*30/i],
    ['«не меньше 0»', /не\s+меньше\s+0\s*₽/i],
    ['«задаток / невозвратный / депозит»', /задат(?:ок|к)|невозвратн|депозит/i],
    ['туристический налог на странице услуги', /туристическ/i],
  ])('has no %s in strings or markup', (_name, rx) => {
    const offenders = GUEST.filter(([p, src]) => {
      // StayTouristTaxNotice легально показывается на странице ДОМА и брони дома — не услуги
      if (/туристическ/.test(rx.source) && /(HousePage|BookingPage|CatalogPage|CompanyPage|MyBookings|StayNotice|stayTexts|BookingForm|HouseCard)/.test(p)) return false
      return rx.test(stripComments(src))
    }).map(([p]) => p)
    expect(offenders).toEqual([])
  })
})
