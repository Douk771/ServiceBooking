import { SlotVerticalProvider } from '@/components/slots/SlotVerticalContext'
import { CabinetServiceCreateView } from '@/components/slots/views/CabinetServiceCreateView'
import { staysVertical } from '../../vertical'

/** The page is the shared view inside the «Дома» vertical (ARCHITECTURE_CYCLE42.md §42.12.1). */
export function ServiceCreatePage() {
  return (
    <SlotVerticalProvider value={staysVertical}>
      <CabinetServiceCreateView />
    </SlotVerticalProvider>
  )
}
