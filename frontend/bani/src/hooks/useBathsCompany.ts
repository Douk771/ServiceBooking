import type { SlotCabinetCompany } from '@/components/slots/SlotVerticalContext'

/**
 * The company of `/cabinet/:companyId/*` for the shared cabinet screens (`SlotVertical.useCabinetCompany`). FE-42-5 builds
 * `CompanyLayout` and replaces this body with a read of its context; until then the cabinet routes are placeholders and
 * nothing calls it.
 */
export function useBathsCompany(): SlotCabinetCompany {
  throw new Error('useBathsCompany: the cabinet layout (FE-42-5) is not mounted yet')
}
