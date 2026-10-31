import { describe, it, expect } from 'vitest'
import planCardSource from './PlanCard.tsx?raw'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { PlanCard } from './PlanCard'
import type { PricingPlanView } from './pricingLine'

const plan: PricingPlanView = {
  id: 'p', name: 'Лавка', description: 'Описание', pricePerMonth: 690, highlights: ['Преимущество'],
  sortOrder: 1, isFree: false, isTrial: false, limitLines: ['до 1 магазина', 'Заказы без ограничений'],
}

describe('PlanCard (T38-12)', () => {
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
  it('does not repeat the plan name with the badge when the trial is called «Пробный период»', () => {
    render(<PlanCard plan={{ ...plan, name: 'Пробный период', isTrial: true }} />)
    expect(screen.getAllByText('Пробный период')).toHaveLength(1)
    expect(screen.getByRole('heading', { level: 3, name: 'Пробный период' })).toBeInTheDocument()
  })
  it('renders the action as a 44px link with the plan in its accessible name, and no button without an action', () => {
    const { rerender } = render(
      <MemoryRouter>
        <PlanCard plan={plan} action={{ to: '/register', label: 'Подключить', ariaLabel: 'Подключить тариф «Лавка»' }} />
      </MemoryRouter>,
    )
    const link = screen.getByRole('link', { name: 'Подключить тариф «Лавка»' })
    expect(link).toHaveAttribute('href', '/register')
    expect(link).toHaveTextContent('Подключить')
    expect(link.className).toContain('min-h-[44px]')
    rerender(<MemoryRouter><PlanCard plan={plan} /></MemoryRouter>)
    expect(screen.queryByRole('link')).toBeNull()
  })
  it('has no hard-coded service-specific limit words', () => {
    expect(planCardSource).not.toMatch(/компании|сотрудники/)
  })
})
