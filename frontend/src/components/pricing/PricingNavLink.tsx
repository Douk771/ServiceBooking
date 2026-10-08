import { Link } from 'react-router-dom'
import { usePricingGrid, type PricingLine } from './pricingLine'

interface Props {
  className: string
  onClick?: () => void
  line: PricingLine
}

/**
 * Nav-bar link to `/pricing`. Renders nothing while there is no grid (404 / loading / error) — a link to a page that
 * then shows "not published yet" is worse than no link (API_CONTRACT_CYCLE7.md §39). Shares the line's query cache with
 * LandingPricingSection/PricingPageBody (ARCHITECTURE_CYCLE38.md §38.7.2).
 */
export function PricingNavLink({ className, onClick, line }: Props) {
  const { data, isSuccess } = usePricingGrid(line)

  if (!isSuccess || !data) return null

  return (
    <Link to="/pricing" className={className} onClick={onClick}>
      Тарифы
    </Link>
  )
}
