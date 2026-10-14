import { SlotVerticalProvider } from '@/components/slots/SlotVerticalContext'
import { CabinetServicesView } from '@/components/slots/views/CabinetServicesView'
import { staysVertical } from '../../vertical'

/** The page is the shared view inside the «Дома» vertical (ARCHITECTURE_CYCLE42.md §42.12.1). */
export function ServicesPage() {
  return (
    <SlotVerticalProvider value={staysVertical}>
      <CabinetServicesView />
    </SlotVerticalProvider>
  )
}
