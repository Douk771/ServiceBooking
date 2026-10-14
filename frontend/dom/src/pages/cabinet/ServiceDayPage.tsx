import { SlotVerticalProvider } from '@/components/slots/SlotVerticalContext'
import { CabinetServiceDayView } from '@/components/slots/views/CabinetServiceDayView'
import { staysVertical } from '../../vertical'

/** The page is the shared view inside the «Дома» vertical (ARCHITECTURE_CYCLE42.md §42.12.1). */
export function ServiceDayPage() {
  return (
    <SlotVerticalProvider value={staysVertical}>
      <CabinetServiceDayView />
    </SlotVerticalProvider>
  )
}
