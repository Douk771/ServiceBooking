import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CAROUSEL_INTERVAL_MS, LandingScreenshotCarousel } from './LandingScreenshotCarousel'
import type { LandingScreenshotCarousel as Carousel } from './types'

const shot = (n: number) => ({ src: `/s${n}.webp`, width: 300, height: 500, alt: `alt ${n}`, caption: `${n}. Шаг ${n}` })
const carousel: Carousel = { label: 'Как проходит запись', slides: [shot(1), shot(2), shot(3)] }
const active = () => screen.getAllByTestId('carousel-slide').findIndex((el) => el.dataset.active)

function mockReducedMotion(reduce: boolean) {
  window.matchMedia = vi.fn().mockImplementation((q: string) => ({
    matches: reduce && q.includes('reduce'), media: q, addEventListener: vi.fn(), removeEventListener: vi.fn(),
  })) as unknown as typeof window.matchMedia
}

describe('LandingScreenshotCarousel', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    mockReducedMotion(false)
  })
  afterEach(() => vi.useRealTimers())

  it('is a labelled region with every slide rendered and the first one active', () => {
    render(<LandingScreenshotCarousel carousel={carousel} />)
    expect(screen.getByRole('region', { name: 'Как проходит запись' })).toBeInTheDocument()
    expect(screen.getAllByTestId('carousel-slide')).toHaveLength(3)
    expect(active()).toBe(0)
  })

  it('advances by itself and wraps around', () => {
    render(<LandingScreenshotCarousel carousel={carousel} />)
    act(() => vi.advanceTimersByTime(CAROUSEL_INTERVAL_MS))
    expect(active()).toBe(1)
    act(() => vi.advanceTimersByTime(CAROUSEL_INTERVAL_MS))
    act(() => vi.advanceTimersByTime(CAROUSEL_INTERVAL_MS))
    expect(active()).toBe(0)
  })

  it('arrows and dots switch slides', () => {
    render(<LandingScreenshotCarousel carousel={carousel} />)
    fireEvent.click(screen.getByRole('button', { name: 'Предыдущий кадр' }))
    expect(active()).toBe(2)
    fireEvent.click(screen.getByRole('button', { name: 'Следующий кадр' }))
    expect(active()).toBe(0)
    fireEvent.click(screen.getByRole('button', { name: 'Кадр 2: 2. Шаг 2' }))
    expect(active()).toBe(1)
    expect(screen.getByRole('button', { name: 'Кадр 2: 2. Шаг 2' })).toHaveAttribute('aria-current', 'true')
  })

  it('pause button stops autoplay', () => {
    render(<LandingScreenshotCarousel carousel={carousel} />)
    fireEvent.click(screen.getByRole('button', { name: 'Остановить автолистание' }))
    act(() => vi.advanceTimersByTime(CAROUSEL_INTERVAL_MS * 3))
    expect(active()).toBe(0)
    expect(screen.getByRole('button', { name: 'Листать автоматически' })).toBeInTheDocument()
  })

  it('does not autoplay when the system asks to reduce motion', () => {
    mockReducedMotion(true)
    render(<LandingScreenshotCarousel carousel={carousel} />)
    act(() => vi.advanceTimersByTime(CAROUSEL_INTERVAL_MS * 3))
    expect(active()).toBe(0)
  })

  it('hover pauses autoplay', () => {
    render(<LandingScreenshotCarousel carousel={carousel} />)
    fireEvent.mouseEnter(screen.getByRole('region'))
    act(() => vi.advanceTimersByTime(CAROUSEL_INTERVAL_MS * 2))
    expect(active()).toBe(0)
  })

  it('swipe left goes to the next slide', () => {
    render(<LandingScreenshotCarousel carousel={carousel} />)
    const track = screen.getAllByTestId('carousel-slide')[0].parentElement!
    // jsdom не знает PointerEvent и теряет clientX у fireEvent.pointer*: шлём MouseEvent с типом pointer-события.
    act(() => {
      track.dispatchEvent(new MouseEvent('pointerdown', { clientX: 200, bubbles: true }))
      track.dispatchEvent(new MouseEvent('pointerup', { clientX: 100, bubbles: true }))
    })
    expect(active()).toBe(1)
  })

  it('highlights the step of the active slide when autoplay advances', () => {
    render(<LandingScreenshotCarousel carousel={carousel} />)
    expect(screen.getByRole('button', { name: 'Кадр 1: 1. Шаг 1' })).toHaveAttribute('aria-current', 'true')
    act(() => vi.advanceTimersByTime(CAROUSEL_INTERVAL_MS))
    expect(screen.getByRole('button', { name: 'Кадр 2: 2. Шаг 2' })).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('button', { name: 'Кадр 1: 1. Шаг 1' })).not.toHaveAttribute('aria-current')
  })

  it('desktop layout: two columns, step list with description of the current step only', () => {
    render(<LandingScreenshotCarousel carousel={carousel} />)
    const region = screen.getByRole('region')
    expect(region.className).toMatch(/md:grid-cols-\[/)
    const steps = region.querySelectorAll('ol > li')
    expect(steps).toHaveLength(3)
    expect(steps[0].textContent).toContain('alt 1')
    expect(steps[1].textContent).not.toContain('alt 2')
    fireEvent.click(screen.getByRole('button', { name: 'Кадр 2: 2. Шаг 2' }))
    expect(steps[1].textContent).toContain('alt 2')
    expect(steps[0].textContent).not.toContain('alt 1')
  })

  it('screenshot height is capped (<=560px and <=60-65vh)', () => {
    render(<LandingScreenshotCarousel carousel={carousel} />)
    const img = screen.getAllByRole('img', { hidden: true })[0]
    const m = img.className.match(/h-\[min\((\d+)vh,(\d+)px\)\]/)
    expect(m).not.toBeNull()
    expect(Number(m![1])).toBeLessThanOrEqual(65)
    expect(Number(m![2])).toBeLessThanOrEqual(560)
  })

  it('pause toggle and arrows keep the active step highlighted', () => {
    render(<LandingScreenshotCarousel carousel={carousel} />)
    fireEvent.click(screen.getByRole('button', { name: 'Следующий кадр' }))
    expect(screen.getByRole('button', { name: 'Кадр 2: 2. Шаг 2' })).toHaveAttribute('aria-current', 'true')
    fireEvent.click(screen.getByRole('button', { name: 'Предыдущий кадр' }))
    fireEvent.click(screen.getByRole('button', { name: 'Предыдущий кадр' }))
    expect(screen.getByRole('button', { name: 'Кадр 3: 3. Шаг 3' })).toHaveAttribute('aria-current', 'true')
  })
})
