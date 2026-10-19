import type { IconName } from '@/components/ui/Icon'
import type { SiteKey } from '@/utils/publicSites'

// API_CONTRACT_CYCLE44_PRODUCTS_LANDING.md §44.23.
export type ProductKey = Exclude<SiteKey, 'about'>
export type ProductStatus = 'live' | 'testing' | 'hidden'
/** Relative path, no scheme and no host; '//' is forbidden by a test. */
export type RelativePath = `/${string}`

export interface ProductAction {
  label: string
  path: RelativePath
}
export interface ChooserRow {
  business: string
}

interface ProductBase {
  key: ProductKey
  name: string
  icon: IconName
  forWhom: string
  features: readonly [string, string, string, ...string[]]
  forClients: string
  clientAction: ProductAction
  chooserRows?: readonly ChooserRow[]
}
export interface LiveProduct extends ProductBase {
  status: 'live'
  businessAction: ProductAction
  testingNote?: never
}
export interface TestingProduct extends ProductBase {
  status: 'testing'
  businessAction?: never
  testingNote: string
}
export interface HiddenProduct extends ProductBase {
  status: 'hidden'
  businessAction?: ProductAction
  testingNote?: string
}
export type ProductCard = LiveProduct | TestingProduct | HiddenProduct

export interface PageCopy {
  meta: { title: string; description: string }
  brand: 'EZBOOK'
  hero: { title: string; subtitle: string }
  productsHeading: string
  testingBadge: 'Тестовый режим'
  account: { title: string; paragraphs: readonly [string, ...string[]] }
  chooser?: { title: string }
  footer: { legalLabel: string; legalPath: RelativePath }
  notFound: { title: string; text: string; backLabel: string }
}
