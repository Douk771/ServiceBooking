import { useOutletContext } from 'react-router-dom'
import type { BathsCompanyManageDto } from './types'

/** What `CompanyLayout` hands to every screen under `/cabinet/:companyId/*` through the router outlet. */
export interface CabinetOutletContext {
  company: BathsCompanyManageDto
  refresh: () => void
}

/** The company of `/cabinet/:companyId/*`, loaded once by `CompanyLayout`. */
export function useBathsCompany(): CabinetOutletContext {
  return useOutletContext<CabinetOutletContext>()
}
