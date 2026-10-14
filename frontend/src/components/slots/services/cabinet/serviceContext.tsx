import { createContext, useContext, type ReactNode } from 'react'
import type { ServiceManageDto } from '@/types/slots'

export interface ServiceTabContext {
  companyId: string
  service: ServiceManageDto
  /** Puts the server's fresh service into the cache after a save (the setup, publish and photo routes answer with the whole DTO). */
  setService: (s: ServiceManageDto) => void
  /** Setup, prices, positions, weekly template, publish (`ManageServices`). */
  canManage: boolean
  /** Description and photos (`EditServiceContent`). */
  canEditContent: boolean
  /** Manual dates (`ManageServiceDates`). */
  canManageDates: boolean
}

const Ctx = createContext<ServiceTabContext | null>(null)

export function ServiceTabProvider({ value, children }: { value: ServiceTabContext; children: ReactNode }) {
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

// Provider and hook share one file by design (like houseContext); the context is never hot-swapped.
// eslint-disable-next-line react-refresh/only-export-components
export function useServiceTab(): ServiceTabContext {
  const v = useContext(Ctx)
  if (!v) throw new Error('useServiceTab must be used inside the service page')
  return v
}
