import { PricingPageBody } from '../components/pricing/PricingPageBody'
import { zapisPricingLine } from '../components/pricing/zapisPricingLine'

/** Public route `/pricing` «Записи»; вся логика — в PricingPageBody (ARCHITECTURE_CYCLE38.md §38.7.2). */
export function PricingPage() {
  return (
    <PricingPageBody
      line={zapisPricingLine}
      site="zapis"
      documentTitle="Тарифы и цены — EZBOOK"
      cta={{ kind: 'auth-route', guestTo: '/register', authedTo: '/register', label: 'Зарегистрировать компанию' }}
      // Кнопки карточек: гость — регистрация (как нижний CTA); вошедший владелец — «Ваша подписка» (/billing, там заявка на тариф),
      // остальные вошедшие — /cabinet (без компании он сам ведёт на /cabinet/new). Выбранный тариф не передаётся: /billing параметра не читает.
      planCta={{ guestTo: '/register', paidAuthedTo: '/billing', freeAuthedTo: '/cabinet', paidRoles: ['CompanyOwner', 'SuperAdmin'] }}
    />
  )
}
