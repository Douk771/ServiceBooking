import { useQuery } from '@tanstack/react-query'
import { companiesApi } from '../../api/companies'

/**
 * Cycle 23, ARCHITECTURE_CYCLE23.md §389.3-3 — shops no longer show up in the ezbook cabinet (its lists default
 * to `kind=Services`), so a user who has some is pointed to goods. Renders nothing while loading, on error, or when
 * there are no shops — a missing hint must never break the cabinet.
 */
export function GoodsShopsNotice() {
  const { data } = useQuery({ queryKey: ['kinds-summary'], queryFn: companiesApi.getKindsSummary, retry: false })
  if (!data || data.orders.count <= 0) return null
  return (
    <p className="text-sm text-ink-soft bg-cream-deep rounded-xl px-4 py-3 mb-6" data-testid="goods-shops-notice">
      Ваши магазины управляются на{' '}
      <a href={data.orders.siteUrl} className="text-gold font-semibold hover:text-gold-dark">
        goods.ezbook.ru
      </a>
      .
    </p>
  )
}
