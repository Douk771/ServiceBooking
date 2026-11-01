import { describe, it, expect, vi } from 'vitest'
import { QueryClient } from '@tanstack/react-query'
import { invalidateSalonCatalogListing } from './salonCatalogListingCache'

describe('invalidateSalonCatalogListing', () => {
  it('CY49-F2: invalidates only the listing key of the given company', () => {
    const qc = new QueryClient()
    const spy = vi.spyOn(qc, 'invalidateQueries')
    invalidateSalonCatalogListing(qc, 'co7')
    expect(spy).toHaveBeenCalledWith({ queryKey: ['company-catalog-listing', 'co7'] })
  })
})
