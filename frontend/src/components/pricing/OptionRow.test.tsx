import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { OptionRow } from './OptionRow'
import type { PricingOptionDto } from '../../types/pricing'

const baseOption: PricingOptionDto = {
  id: 'opt-1',
  name: 'SMS-напоминания',
  kind: 'Quantity',
  pricePerMonth: 3,
  unitName: 'сообщение',
  unitPriceText: '3 ₽/сообщение',
  sortOrder: 1,
}

describe('OptionRow', () => {
  it('prints unitPriceText verbatim for a Quantity option', () => {
    render(<OptionRow option={baseOption} />)
    expect(screen.getByText('3 ₽/сообщение')).toBeInTheDocument()
  })

  it('falls back to the plain monthly price when the server sends unitPriceText: null (Toggle options)', () => {
    const toggleOption: PricingOptionDto = {
      ...baseOption,
      kind: 'Toggle',
      unitName: null,
      unitPriceText: null,
      pricePerMonth: 690,
    }
    render(<OptionRow option={toggleOption} />)
    expect(screen.getByText('690 ₽/мес')).toBeInTheDocument()
  })

  it('renders the description when present and omits it when absent', () => {
    const { rerender } = render(<OptionRow option={{ ...baseOption, description: 'До 300 в месяц' }} />)
    expect(screen.getByText('До 300 в месяц')).toBeInTheDocument()

    rerender(<OptionRow option={baseOption} />)
    expect(screen.queryByText('До 300 в месяц')).not.toBeInTheDocument()
  })
})
