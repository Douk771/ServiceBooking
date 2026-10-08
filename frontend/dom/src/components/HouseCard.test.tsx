import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { HouseCard } from './HouseCard'
import { catalogItemFixture } from '../test/fixtures'

const filters = { checkIn: null, checkOut: null, guests: 1 }
const renderCard = (over = {}, f: Parameters<typeof HouseCard>[0]['filters'] = filters) =>
  render(
    <MemoryRouter>
      <HouseCard item={catalogItemFixture(over)} filters={f} />
    </MemoryRouter>,
  )

describe('HouseCard (T41-05)', () => {
  it('shows long name, company and address in full without truncation classes', () => {
    const house = 'Д'.repeat(200)
    const company = 'К'.repeat(120)
    const address = 'А'.repeat(300)
    const { container } = renderCard({ houseName: house, companyName: company, address })
    const h3 = container.querySelector('h3')!
    const companyEl = screen.getByText(company)
    const addr = screen.getByText(address)
    expect(h3.textContent).toBe(house)
    expect(companyEl.textContent).toBe(company)
    expect(addr.textContent).toBe(address)
    for (const el of [h3, companyEl, addr]) {
      expect(el.className).not.toMatch(/truncate|line-clamp/)
      expect(el.className).toContain('break-words')
    }
  })

  it('keeps the rest of the card', () => {
    renderCard({ availableForDates: false, capacity: 3, extraBedsMax: 1, dogsForbidden: true, registryNumber: 'РНТ-1' })
    expect(screen.getByText('Занято на выбранные даты')).toBeInTheDocument()
    expect(screen.getByText(/до 4 гостей/)).toBeInTheDocument()
    expect(screen.getByText('· без собак')).toBeInTheDocument()
    expect(screen.getByText('Номер в реестре: РНТ-1')).toBeInTheDocument()
  })

  it('shows three price variants', () => {
    const a = renderCard({ priceFromRub: 5000 })
    expect(a.container.textContent).toContain('от')
    a.unmount()
    const b = renderCard({ priceFromRub: null })
    expect(b.container.textContent).toContain('Цена уточняется')
    b.unmount()
    const c = renderCard({ totalRub: 15000, nights: 3, averageNightRub: 5000 })
    expect(c.container.textContent).toContain('за 3 ночи')
    expect(c.container.textContent).toContain('за ночь')
  })

  it('link carries the dates and adults', () => {
    renderCard({}, { checkIn: '2027-01-05', checkOut: '2027-01-08', guests: 3 })
    const href = screen.getByRole('link').getAttribute('href')!
    expect(href).toContain('checkIn=2027-01-05')
    expect(href).toContain('checkOut=2027-01-08')
    expect(href).toContain('adults=3')
  })
})
