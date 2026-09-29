import { api } from './client'
import type { AddressNoticeResultDto, Company } from '../types'

/**
 * API_CONTRACT_CYCLE19.md §413.4 — `CompanyAddressUpdateResultDto` is `{ company: CompanyDto }`.
 * Kept as a named type because `Company` is a hand-maintained interface, not read off the
 * generated schema (§388.3).
 */
export interface SaveCompanyAddressResult {
  company: Company
}

export const companyAddressApi = {
  /**
   * `PUT /api/companies/{id}/address` — writes the address. ARCHITECTURE_CYCLE19.md §388.2/§413.2:
   * `verify` is no longer sent — the geocoder is gone, and the route never wrote the five
   * verification columns for anything this component controls. An empty `address` erases it
   * (§234, unchanged) — pass `''`, not `undefined`, to do that.
   */
  saveAddress: (companyId: string, address: string) =>
    api.put<SaveCompanyAddressResult>(`/companies/${companyId}/address`, { address }).then((r) => r.data),

  /**
   * `POST /api/companies/address/notice` — records that the owner/SuperAdmin saw and accepted the
   * public-address warning (§220/§242, unchanged by cycle 19). `textVersion` MUST be the version the
   * caller actually read from `GET /api/legal/texts/PublicAddressNotice` — never a hardcoded string,
   * so a 409 (text changed under them) is meaningful.
   */
  confirmNotice: (textVersion: string) =>
    api.post<AddressNoticeResultDto>('/companies/address/notice', { textVersion, confirmed: true }).then((r) => r.data),
}
