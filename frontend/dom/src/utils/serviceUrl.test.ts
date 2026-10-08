// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { parseServiceUrl } from './serviceUrl'

describe('parseServiceUrl', () => {
  it('reads the company and the service address', () => {
    expect(parseServiceUrl('/kedr-park/uslugi/banya')).toEqual({ companySlug: 'kedr-park', serviceSlug: 'banya' })
    expect(parseServiceUrl('/kedr-park/uslugi/banya/?date=2027-01-15')).toEqual({ companySlug: 'kedr-park', serviceSlug: 'banya' })
  })

  it('rejects the address of a house, a company or a booking', () => {
    expect(parseServiceUrl('/kedr-park/kedr')).toBeNull()
    expect(parseServiceUrl('/kedr-park')).toBeNull()
    expect(parseServiceUrl('/b/token')).toBeNull()
  })
})
