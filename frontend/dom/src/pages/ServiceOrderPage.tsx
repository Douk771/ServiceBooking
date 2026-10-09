import { SlotVerticalProvider } from '@/components/slots/SlotVerticalContext'
import { ServiceOrderView } from '@/components/slots/views/ServiceOrderView'
import { staysVertical } from '../vertical'

/** The page is the shared view inside the «Дома» vertical (ARCHITECTURE_CYCLE42.md §42.12.1). */
export function ServiceOrderPage() {
  return (
    <SlotVerticalProvider value={staysVertical}>
      <ServiceOrderView />
    </SlotVerticalProvider>
  )
}
