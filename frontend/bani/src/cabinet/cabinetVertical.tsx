import { useOutletContext } from 'react-router-dom'
import type { SlotCabinetCompany, SlotVertical } from '@/components/slots/SlotVerticalContext'
import { bathsVertical } from '../vertical'
import { PHOTO_PEOPLE_FALLBACK, RESOURCE_ONE_PLACE_HINT } from './cabinetTexts'
import type { BathsCompanyManageDto } from './types'

export interface CabinetOutletContext {
  company: BathsCompanyManageDto
  refresh: () => void
}

/** The company of `/cabinet/:companyId/*`, loaded once by `CompanyLayout`. */
export function useBathsCompany(): CabinetOutletContext {
  return useOutletContext<CabinetOutletContext>()
}

const useCabinetCompany = (): SlotCabinetCompany => useBathsCompany()

/**
 * `bathsVertical` of FE-42-3 plus what only the cabinet needs: the company of the layout's outlet (FE-42-5 may swap its own hook in),
 * the notice above the positions (Т42-05), the «no people in the frame» line (Т42-13) and the R42-1 hint on creating a resource.
 */
export const bathsCabinetVertical: SlotVertical = {
  ...bathsVertical,
  legal: {
    ...bathsVertical.legal,
    keys: { ...bathsVertical.legal.keys, photoPeopleNotice: 'CompanyPhotoPeopleNotice', positionsOwnerNotice: 'BathPositionsOwnerNotice' },
    fallbacks: { ...bathsVertical.legal.fallbacks, ...PHOTO_PEOPLE_FALLBACK },
  },
  words: { ...bathsVertical.words, serviceCreateHint: RESOURCE_ONE_PLACE_HINT },
  useCabinetCompany,
}
