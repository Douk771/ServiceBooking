import { ServiceView } from '@/components/slots/views/ServiceView'
import { BathsGuestScope } from '../components/BathsGuestScope'

/**
 * `/:slug/:resourceSlug` — a bath, sauna or tub (US-42-13…15). The shared page of a service with the capacity, «Время местное, <город>»,
 * the order form (guests, notice, terms, captcha, the messenger tick off by default) — all of it is the shared view in the bani vertical.
 */
export function ResourcePage() {
  return (
    <BathsGuestScope>
      <ServiceView />
    </BathsGuestScope>
  )
}
