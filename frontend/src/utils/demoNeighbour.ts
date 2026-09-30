import type { DemoProduct, DemoStatusDto } from '../api/demo'

/** US-35-10 — the link to the OTHER demo stand: ezbook points at «Заказы», goods at «Запись». Labels are fixed by the
 *  contract wording (API_CONTRACT_CYCLE35.md §35.21, `siteUrls`); the address comes from the server. */
const NEIGHBOUR: Record<DemoProduct, { target: DemoProduct; label: string }> = {
  services: { target: 'orders', label: 'Посмотреть демо «Заказов»' },
  orders: { target: 'services', label: 'Посмотреть демо «Записи»' },
}

/** `null` when the status has no usable address (an older API without `siteUrls`): no link rather than a broken one. */
export function neighbourDemoLink(
  product: DemoProduct,
  siteUrls: DemoStatusDto['siteUrls'] | undefined,
): { href: string; label: string } | null {
  const { target, label } = NEIGHBOUR[product]
  const href = siteUrls?.[target]?.trim()
  return href && /^https?:\/\//i.test(href) ? { href, label } : null
}
