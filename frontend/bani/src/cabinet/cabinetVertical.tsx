import type { SlotCabinetCompany, SlotVertical } from '@/components/slots/SlotVerticalContext'
import { bathsVertical } from '../vertical'
import { PHOTO_PEOPLE_FALLBACK, RESOURCE_ONE_PLACE_HINT } from './cabinetTexts'
import { useBathsCompany, type CabinetOutletContext } from './useBathsCompany'

// The hook lives in its own file (no import of `vertical.ts`), so `vertical.ts` can use it without a cycle; screens import it from here.
export { useBathsCompany }
export type { CabinetOutletContext }

const useCabinetCompany = (): SlotCabinetCompany => useBathsCompany()

/**
 * `bathsVertical` of FE-42-3 plus what only the cabinet needs: the company of the layout's outlet (`useBathsCompany`),
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
