// @vitest-environment node
// CY44-07 (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.3.3): the committed production addresses equal PublicSites in appsettings.json.
import { describe, it, expect } from 'vitest'
import appsettings from '../../../ServiceBooking.API/appsettings.json'
import { PRODUCTION_SITE_URLS } from './publicSites'

describe('CY44-07 publicSites matches backend PublicSites', () => {
  const backend = (appsettings as { PublicSites: Record<string, string> }).PublicSites
  it.each([
    ['zapis', 'ServicesBaseUrl'],
    ['goods', 'OrdersBaseUrl'],
    ['dom', 'StaysBaseUrl'],
    ['bani', 'BathsBaseUrl'],
  ] as const)('%s <-> PublicSites:%s', (key, option) => {
    expect(PRODUCTION_SITE_URLS[key]).toBe(backend[option])
  })
})
