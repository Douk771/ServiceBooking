import { useOutletContext } from 'react-router-dom'
import type { StaysCompanyManageWithServices } from '../types'

export const companyKey = (companyId: string) => ['stays-company', companyId] as const

export interface CompanyContext {
  company: StaysCompanyManageWithServices
  /** Re-reads the company (after a save that changes the checklist, the gate, the plan or the rights). */
  refresh: () => void
}

/** The company of `/cabinet/:companyId/*`, loaded once by `CompanyLayout`. */
export function useStaysCompany(): CompanyContext {
  return useOutletContext<CompanyContext>()
}
