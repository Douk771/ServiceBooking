import { SlotVerticalProvider } from '@/components/slots/SlotVerticalContext'
import { CabinetServiceEditView } from '@/components/slots/views/CabinetServiceEditView'
import { staysVertical } from '../../vertical'

/** The page is the shared view inside the «Дома» vertical (ARCHITECTURE_CYCLE42.md §42.12.1). */
export function ServiceEditPage() {
  return (
    <SlotVerticalProvider value={staysVertical}>
      <CabinetServiceEditView />
    </SlotVerticalProvider>
  )
}
