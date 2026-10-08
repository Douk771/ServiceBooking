import { PricingPageBody } from '../components/pricing/PricingPageBody'
import { zapisPricingLine } from '../components/pricing/zapisPricingLine'

/** Public route `/pricing` «Записи»; вся логика — в PricingPageBody (ARCHITECTURE_CYCLE38.md §38.7.2). */
export function PricingPage() {
  return (
    <PricingPageBody
      line={zapisPricingLine}
      documentTitle="Тарифы и цены — EZBOOK"
      cta={{ kind: 'auth-route', guestTo: '/register', authedTo: '/register', label: 'Зарегистрировать компанию' }}
    />
  )
}
