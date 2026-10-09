import { SlotVerticalProvider } from '@/components/slots/SlotVerticalContext'
import { CabinetServiceSessionView } from '@/components/slots/views/CabinetServiceSessionView'
import { staysVertical } from '../../vertical'

/** The page is the shared view inside the «Дома» vertical (ARCHITECTURE_CYCLE42.md §42.12.1). */
export function ServiceSessionPage() {
  return (
    <SlotVerticalProvider value={staysVertical}>
      <CabinetServiceSessionView />
    </SlotVerticalProvider>
  )
}
