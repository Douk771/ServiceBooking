import { SlotVerticalProvider } from '@/components/slots/SlotVerticalContext'
import { ServiceView } from '@/components/slots/views/ServiceView'
import { staysVertical } from '../vertical'

/** The page is the shared view inside the «Дома» vertical (ARCHITECTURE_CYCLE42.md §42.12.1). */
export function ServicePage() {
  return (
    <SlotVerticalProvider value={staysVertical}>
      <ServiceView />
    </SlotVerticalProvider>
  )
}
