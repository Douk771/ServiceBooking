import { api } from './client'
import type { AddressLookupResultDto, AddressNoticeResultDto, Company, CompanyAddressVerificationResultDto } from '../types'

/** API_CONTRACT_CYCLE13.md §233. */
export interface AddressLookupParams {
  address: string
  cityId?: number | null
  companyId?: string | null
}

/** API_CONTRACT_CYCLE13.md §234 — `company` comes back as the full `CompanyDto`. */
export interface SaveCompanyAddressResult {
  company: Company
  verification: CompanyAddressVerificationResultDto
}

export const companyAddressApi = {
  /**
   * `POST /api/companies/address/lookup` — search only, never writes anything. `[Authorize]`; when
   * `companyId` is omitted (company-creation flow) any authenticated user may call it. Can 404 for
   * two indistinguishable reasons: the switch is off (`Provider: logging`), or `companyId` isn't the
   * caller's to manage — the interface is not meant to hit this at all when it already knows
   * `addressVerification.available === false` (§237).
   */
  lookup: (params: AddressLookupParams) =>
    api.post<AddressLookupResultDto>('/companies/address/lookup', params).then((r) => r.data),

  /**
   * `PUT /api/companies/{id}/address` — the only endpoint that writes verification columns. Always
   * works, even with the switch off (`verify` is then simply ignored server-side, `outcome:
   * "Disabled"`). An empty `address` erases it (§234) — pass `''`, not `undefined`, to do that.
   */
  saveAddress: (companyId: string, address: string, verify = false) =>
    api.put<SaveCompanyAddressResult>(`/companies/${companyId}/address`, { address, verify }).then((r) => r.data),

  /**
   * `POST /api/companies/address/notice` — records that the owner/SuperAdmin saw and accepted the
   * public-address warning (§220/§242). Independent of the geocoder switch. `textVersion` MUST be the
   * version the caller actually read from `GET /api/legal/texts/PublicAddressNotice` — never a
   * hardcoded string, so a 409 (text changed under them) is meaningful.
   */
  confirmNotice: (textVersion: string) =>
    api.post<AddressNoticeResultDto>('/companies/address/notice', { textVersion, confirmed: true }).then((r) => r.data),
}
