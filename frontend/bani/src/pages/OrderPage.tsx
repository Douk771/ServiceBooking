import { ServiceOrderView } from '@/components/slots/views/ServiceOrderView'
import { BathsGuestScope } from '../components/BathsGuestScope'

/** `/s/:token` — a booking by its link (US-42-16): the shared page with the «Напоминание» block and «Забронировать ещё в этом комплексе». */
export function OrderPage() {
  return (
    <BathsGuestScope>
      <ServiceOrderView />
    </BathsGuestScope>
  )
}
