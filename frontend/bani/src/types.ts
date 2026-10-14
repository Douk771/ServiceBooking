import type { components } from '@/types/api-cycle42.generated'
import type { components as C37 } from '@/types/api-cycle37.generated'

/** bani types are read straight off the generated schemas (ARCHITECTURE_CYCLE42.md §42.12.2) — never retyped by hand. */
export type BathsCompanyListItemDto = components['schemas']['BathsCompanyListItemDto']
export type CompanyKindsSummaryDto = C37['schemas']['CompanyKindsSummaryDto']

export type BathResourceCardDto = components['schemas']['BathResourceCardDto']
export type BathsCatalogPageDto = components['schemas']['BathsCatalogPageDto']
export type BathsCatalogCityDto = components['schemas']['BathsCatalogCityDto']
export type BathsCatalogCitiesDto = components['schemas']['BathsCatalogCitiesDto']
export type BathsPublicCompanyDto = components['schemas']['BathsPublicCompanyDto']
