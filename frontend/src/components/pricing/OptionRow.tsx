import type { PricingOptionDto } from '../../types/pricing'
import { formatMonthlyPrice } from '../../utils/pricingFormat'

interface Props {
  option: PricingOptionDto
}

/**
 * Server assembles `unitPriceText` for Quantity options (API_CONTRACT_CYCLE7.md §39, US-71 п. 3 —
 * no bare numbers without a unit anywhere on the page) and this component prints that string as-is.
 * For Toggle options the server always sends `unitPriceText: null` (there is no per-unit name to
 * attach) — fall back to the plain monthly price so the row is never left blank.
 */
export function OptionRow({ option }: Props) {
  const priceText = option.unitPriceText ?? formatMonthlyPrice(option.pricePerMonth)

  return (
    <div className="flex items-center justify-between gap-4 py-4 border-b border-line last:border-b-0">
      <div>
        <p className="text-[15px] font-medium text-ink">{option.name}</p>
        {option.description && <p className="text-sm text-ink-soft mt-0.5">{option.description}</p>}
      </div>
      <span className="text-sm font-medium text-ink-soft whitespace-nowrap shrink-0">{priceText}</span>
    </div>
  )
}
