/**
 * `PublicServiceSummaryDto.url` / `BookingServiceOptionDto.url` is `/<companySlug>/uslugi/<serviceSlug>` (API_CONTRACT_CYCLE39.md
 * §39.21.2). The lists of services of a stay do not carry the positions of a service, so the picker reads the service page by this
 * address. null when the address is not a service address.
 */
export function parseServiceUrl(url: string): { companySlug: string; serviceSlug: string } | null {
  const path = url.split(/[?#]/)[0]
  const m = /^\/([^/]+)\/uslugi\/([^/]+)\/?$/.exec(path)
  return m ? { companySlug: decodeURIComponent(m[1]), serviceSlug: decodeURIComponent(m[2]) } : null
}
