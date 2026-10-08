import type { CompanyKind } from '../types'

/** Cycle 23 — labels for the company kind in the admin list (US-23-28). Absent kind = salon (older servers). */
export function companyKindLabel(kind: CompanyKind | undefined): string {
  return kind === 'Orders' ? 'Магазин' : kind === 'Stays' ? 'Дома' : 'Салон'
}

export type CompanyKindFilter = 'all' | CompanyKind

export const COMPANY_KIND_FILTERS: { value: CompanyKindFilter; label: string }[] = [
  { value: 'all', label: 'Все' },
  { value: 'Services', label: 'Салоны' },
  { value: 'Orders', label: 'Магазины' },
  { value: 'Stays', label: 'Дома' },
]

/** Filter value → `?kind=` (undefined = not sent = all kinds, §408.6). */
export function kindParam(filter: CompanyKindFilter): CompanyKind | undefined {
  return filter === 'all' ? undefined : filter
}

/** `Orders` companies live on goods: their public page is `publicUrl`, not `/company/<slug>`. */
export function isShop(company: { kind?: CompanyKind }): boolean {
  return company.kind === 'Orders'
}
