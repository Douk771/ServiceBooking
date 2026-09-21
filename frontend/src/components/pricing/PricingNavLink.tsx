import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { pricingApi } from '../../api/pricing'

interface Props {
  className: string
  onClick?: () => void
}

/**
 * Nav-bar link to `/pricing`. Renders nothing while the publication switch is off (404) — a link to
 * a page that then shows "not published yet" is worse than no link (API_CONTRACT_CYCLE5.md §39).
 * Shares the `['public-pricing']` query cache with PricingTeaser/PricingPage, so navigating in stays
 * instant.
 */
export function PricingNavLink({ className, onClick }: Props) {
  const { data, isSuccess } = useQuery({
    queryKey: ['public-pricing'],
    queryFn: pricingApi.getPublicPricing,
    retry: false,
    staleTime: 60_000,
  })

  if (!isSuccess || !data) return null

  return (
    <Link to="/pricing" className={className} onClick={onClick}>
      Тарифы
    </Link>
  )
}
