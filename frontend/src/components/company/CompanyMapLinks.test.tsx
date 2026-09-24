import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { CompanyMapLinks } from './CompanyMapLinks'

describe('CompanyMapLinks — ARCHITECTURE_CYCLE13.md §205.1/§239 (licence conditions)', () => {
  it('renders nothing when there is no address (US-132/133)', () => {
    const { container } = render(<CompanyMapLinks address={null} cityName="Барнаул" />)
    expect(container).toBeEmptyDOMElement()
  })

  it('exposes exactly the accessible names the legal review requires', () => {
    render(<CompanyMapLinks address="Ленина, 5" cityName="Барнаул" />)
    expect(screen.getByRole('link', { name: /^Открыть в Яндекс Картах/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /^Открыть в 2ГИС/ })).toBeInTheDocument()
  })

  it('opens both links in a new tab with noopener and noreferrer', () => {
    render(<CompanyMapLinks address="Ленина, 5" cityName="Барнаул" />)
    for (const link of screen.getAllByRole('link')) {
      expect(link).toHaveAttribute('target', '_blank')
      const rel = link.getAttribute('rel') ?? ''
      expect(rel).toContain('noopener')
      expect(rel).toContain('noreferrer')
    }
  })

  it('renders no <img> anywhere — no service logos or branding, text labels only', () => {
    const { container } = render(<CompanyMapLinks address="Ленина, 5" cityName="Барнаул" />)
    expect(container.querySelectorAll('img')).toHaveLength(0)
  })

  it('does not claim an integration or partnership anywhere in the component', () => {
    const { container } = render(<CompanyMapLinks address="Ленина, 5" cityName="Барнаул" />)
    const text = container.textContent ?? ''
    expect(text).not.toMatch(/интеграц/i)
    expect(text).not.toMatch(/партнёр/i)
    expect(text).not.toMatch(/работает на/i)
  })

  it('the URLs carry only the city and address — no utm, ref, or ids', () => {
    render(<CompanyMapLinks address="Ленина, 5" cityName="Барнаул" />)
    for (const link of screen.getAllByRole('link')) {
      const href = link.getAttribute('href') ?? ''
      expect(href).not.toMatch(/utm|ref=|clientid|userid/i)
    }
  })
})
