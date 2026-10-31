import { PricingPageBody } from '@/components/pricing/PricingPageBody'
import { ordersPricingLine } from '../pricing/ordersPricingLine'

/** Public route `/pricing` «Заказов» (ARCHITECTURE_CYCLE38.md §38.7.2); вся логика — в общем PricingPageBody. */
export function PricingPage() {
  return (
    <PricingPageBody
      line={ordersPricingLine}
      site="goods"
      documentTitle="Тарифы и цены — EZBOOK Заказы"
      cta={{ kind: 'auth-route', guestTo: '/register?returnTo=%2Fcabinet%2Fnew', authedTo: '/cabinet/new', label: 'Подключить магазин' }}
      // Кнопки карточек: гость — регистрация (как нижний CTA); вошедший — «Ваша подписка» в кабинете или сам кабинет.
      // Выбранный тариф не передаётся: /cabinet/subscription параметра не читает.
      planCta={{ guestTo: '/register?returnTo=%2Fcabinet%2Fnew', paidAuthedTo: '/cabinet/subscription', freeAuthedTo: '/cabinet' }}
    />
  )
}
