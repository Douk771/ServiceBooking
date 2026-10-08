import { describe, it, expect } from 'vitest'
import planCardSource from './PlanCard.tsx?raw'
import { render, screen } from '@testing-library/react'
import { PlanCard } from './PlanCard'
import type { PricingPlanView } from './pricingLine'

const plan: PricingPlanView = {
  id: 'p', name: 'Лавка', description: 'Описание', pricePerMonth: 690, highlights: ['Преимущество'],
  sortOrder: 1, isFree: false, isTrial: false, limitLines: ['до 1 магазина', 'Заказы без ограничений'],
}

describe('PlanCard (T37-12)', () => {
  it('prints limitLines before highlights', () => {
    render(<PlanCard plan={plan} />)
    expect(screen.getAllByRole('listitem').map((l) => l.textContent)).toEqual(['до 1 магазина', 'Заказы без ограничений', 'Преимущество'])
  })
  it('shows the trial badge only for trial plans', () => {
    const { rerender } = render(<PlanCard plan={plan} />)
    expect(screen.queryByText('Пробный период')).toBeNull()
    rerender(<PlanCard plan={{ ...plan, isTrial: true }} />)
    expect(screen.getByText('Пробный период')).toBeInTheDocument()
  })
  it('has no hard-coded service-specific limit words', () => {
    expect(planCardSource).not.toMatch(/компании|сотрудники/)
  })
})
