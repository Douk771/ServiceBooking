import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { CompanyMapLinks } from './CompanyMapLinks'

// A real 0-bis П2 example — the actual query string an owner would paste (percent-encoded comma
// and all). If this component (or the server validator) ever "normalized" it, the assertion below
// on href equality would catch it.
const YANDEX_URL = 'https://yandex.ru/maps/org/syrovarnya/11766054863/?ll=83.795110%2C53.330510&z=17'
const TWO_GIS_URL = 'https://2gis.ru/barnaul/firm/563478234628539/83.795014%2C53.330486?m=83.795954%2C53.330025%2F17.89'

describe('CompanyMapLinks — ARCHITECTURE_CYCLE15.md §253/§285 (licence conditions, cycle 13 carried forward)', () => {
  it('renders nothing when neither link is filled in (US-132/133)', () => {
    const { container } = render(<CompanyMapLinks yandexUrl={null} twoGisUrl={null} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders only the filled-in link, no empty button for the other one', () => {
    render(<CompanyMapLinks yandexUrl={YANDEX_URL} twoGisUrl={null} />)
    expect(screen.getByRole('link', { name: /^Открыть в Яндекс Картах/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /^Открыть в 2ГИС/ })).not.toBeInTheDocument()
  })

  it('exposes exactly the accessible names the legal review requires', () => {
    render(<CompanyMapLinks yandexUrl={YANDEX_URL} twoGisUrl={TWO_GIS_URL} />)
    expect(screen.getByRole('link', { name: /^Открыть в Яндекс Картах/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /^Открыть в 2ГИС/ })).toBeInTheDocument()
  })

  it('opens both links in a new tab with noopener and noreferrer', () => {
    render(<CompanyMapLinks yandexUrl={YANDEX_URL} twoGisUrl={TWO_GIS_URL} />)
    for (const link of screen.getAllByRole('link')) {
      expect(link).toHaveAttribute('target', '_blank')
      const rel = link.getAttribute('rel') ?? ''
      expect(rel).toContain('noopener')
      expect(rel).toContain('noreferrer')
    }
  })

  it('renders no <img> anywhere — no service logos or branding, text labels only', () => {
    const { container } = render(<CompanyMapLinks yandexUrl={YANDEX_URL} twoGisUrl={TWO_GIS_URL} />)
    expect(container.querySelectorAll('img')).toHaveLength(0)
  })

  it('does not claim an integration or partnership anywhere in the component', () => {
    const { container } = render(<CompanyMapLinks yandexUrl={YANDEX_URL} twoGisUrl={TWO_GIS_URL} />)
    const text = container.textContent ?? ''
    expect(text).not.toMatch(/интеграц/i)
    expect(text).not.toMatch(/партнёр/i)
    expect(text).not.toMatch(/работает на/i)
  })

  it('§285 п.7 — href equals the saved value byte for byte, nothing appended', () => {
    render(<CompanyMapLinks yandexUrl={YANDEX_URL} twoGisUrl={TWO_GIS_URL} />)
    const yandexLink = screen.getByRole('link', { name: /^Открыть в Яндекс Картах/ })
    const twoGisLink = screen.getByRole('link', { name: /^Открыть в 2ГИС/ })
    expect(yandexLink.getAttribute('href')).toBe(YANDEX_URL)
    expect(twoGisLink.getAttribute('href')).toBe(TWO_GIS_URL)
  })
})
