import type { PricingOptionDto } from '../../types/pricing'

interface Props {
  option: PricingOptionDto
}

/**
 * Server assembles `unitPriceText` (API_CONTRACT_CYCLE5.md §39, US-71 п. 3 — no bare numbers without
 * a unit anywhere on the page); this component prints that string verbatim, it does not reformat it.
 */
export function OptionRow({ option }: Props) {
  return (
    <div className="flex items-center justify-between gap-4 py-4 border-b border-line last:border-b-0">
      <div>
        <p className="text-[15px] font-medium text-ink">{option.name}</p>
        {option.description && <p className="text-sm text-ink-soft mt-0.5">{option.description}</p>}
      </div>
      <span className="text-sm font-medium text-ink-soft whitespace-nowrap shrink-0">{option.unitPriceText}</span>
    </div>
  )
}
