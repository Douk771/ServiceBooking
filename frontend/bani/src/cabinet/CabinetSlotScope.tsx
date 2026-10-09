import { Outlet, useOutletContext } from 'react-router-dom'
import { SlotVerticalProvider } from '@/components/slots/SlotVerticalContext'
import { bathsCabinetVertical, type CabinetOutletContext } from './cabinetVertical'

/**
 * Puts the cabinet routes of a company into the «Бани» vertical of the shared service screens and hands the company of
 * `CompanyLayout` further down (a nested <Outlet/> does not inherit the context by itself).
 */
export function CabinetSlotScope() {
  const ctx = useOutletContext<CabinetOutletContext>()
  return (
    <SlotVerticalProvider value={bathsCabinetVertical}>
      <Outlet context={ctx} />
    </SlotVerticalProvider>
  )
}
