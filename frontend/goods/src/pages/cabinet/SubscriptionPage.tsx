import { BillingPage } from '@/pages/BillingPage'

/** Cycle 43 (ARCHITECTURE_CYCLE43.md §43.8.3): the «Мессенджеры» button of the subscription leads to the company's notifications section and opens the wizard of the transport. */
const notificationsHref = (companyId: string, transport: string) => `/cabinet/${companyId}/notifications?connect=${transport}`

/**
 * `/cabinet/subscription` (US-24-24…28) — «Ваша подписка» of the «Заказы» line. The screen is ezbook's own component
 * (ARCHITECTURE_CYCLE24.md §462.2 п.2): same texts and request flow, `line="Orders"` switches it to shops, the monthly order
 * counter and the plans from `availablePlans`. Tariffs of the line are shown only here, inside the cabinet (§490).
 */
export function SubscriptionPage() {
  return <BillingPage line="Orders" notificationsHref={notificationsHref} />
}
