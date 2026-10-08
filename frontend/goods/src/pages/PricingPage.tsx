import { PricingPageBody } from '@/components/pricing/PricingPageBody'
import { ordersPricingLine } from '../pricing/ordersPricingLine'

/** Public route `/pricing` «Заказов» (ARCHITECTURE_CYCLE38.md §38.7.2); вся логика — в общем PricingPageBody. */
export function PricingPage() {
  return (
    <PricingPageBody
      line={ordersPricingLine}
      documentTitle="Тарифы и цены — EZBOOK Заказы"
      cta={{ kind: 'auth-route', guestTo: '/register?returnTo=%2Fcabinet%2Fnew', authedTo: '/cabinet/new', label: 'Подключить магазин' }}
    />
  )
}
