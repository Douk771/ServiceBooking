import { createContext, useContext, type ReactNode } from 'react'
import type { HouseManageDto } from '../../types'

export interface HouseTabContext {
  companyId: string
  house: HouseManageDto
  /** Puts the server's fresh house into the cache after a save (every house route answers with the whole `HouseManageDto`). */
  setHouse: (h: HouseManageDto) => void
  canManage: boolean
  canEditContent: boolean
  /** The company's zone: «today» of the owner's calendars is the company's day, not the phone's. */
  timeZoneId: string
}

const Ctx = createContext<HouseTabContext | null>(null)

export function HouseTabProvider({ value, children }: { value: HouseTabContext; children: ReactNode }) {
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

// Provider and hook share one file by design (like DemoProductContext); the context is never hot-swapped.
// eslint-disable-next-line react-refresh/only-export-components
export function useHouseTab(): HouseTabContext {
  const v = useContext(Ctx)
  if (!v) throw new Error('useHouseTab must be used inside the house page')
  return v
}
