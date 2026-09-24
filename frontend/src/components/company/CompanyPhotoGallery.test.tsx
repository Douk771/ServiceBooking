import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CompanyPhotoGallery } from './CompanyPhotoGallery'
import type { CompanyPhoto } from '../../types'

function photo(overrides: Partial<CompanyPhoto> = {}): CompanyPhoto {
  return {
    id: 'p1',
    url: '/uploads/companies/p1.jpg',
    thumbnailUrl: '/uploads/companies/p1-thumb.jpg',
    width: 1600,
    height: 1067,
    position: 0,
    isCover: true,
    ...overrides,
  }
}

function photos(n: number): CompanyPhoto[] {
  return Array.from({ length: n }, (_, i) =>
    photo({ id: `p${i}`, position: i, isCover: i === 0, url: `/uploads/companies/p${i}.jpg`, thumbnailUrl: `/uploads/companies/p${i}-thumb.jpg` }),
  )
}

function lightboxImg() {
  return screen.getByRole('button', { name: 'Закрыть' }).parentElement!.querySelector('img')!
}

// `aria-hidden="true"` (set on the currently off-screen slides, see review finding below) makes the
// accessible NAME of the element itself compute to `""` — not just its role query visibility — so a
// name-filtered `getByRole` can never find them, `hidden: true` or not. Reach them by their
// `aria-label` attribute directly instead, the way a plain DOM query would.
function slideByLabel(label: string): HTMLElement {
  return screen.getAllByRole('button', { hidden: true }).find((el) => el.getAttribute('aria-label') === label)!
}

describe('CompanyPhotoGallery — ARCHITECTURE_CYCLE13.md §204/§211 (US-131, US-141…US-143)', () => {
  it('renders a plain placeholder, no technical text, when there are no photos', () => {
    render(<CompanyPhotoGallery photos={[]} companyName="Гвоздь" />)
    expect(screen.queryByText('Фото: компания')).not.toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('1 photo: exactly one <img>, no arrows, no indicators', () => {
    render(<CompanyPhotoGallery photos={photos(1)} companyName="Гвоздь" />)
    expect(screen.getAllByRole('img')).toHaveLength(1)
    expect(screen.queryByRole('button', { name: 'Предыдущее фото' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Следующее фото' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Показать фото/ })).not.toBeInTheDocument()
  })

  it.each([2, 3, 4])('%i photos: never renders a "+N" overlay tile (the mosaic is gone)', (n) => {
    render(<CompanyPhotoGallery photos={photos(n)} companyName="Гвоздь" />)
    expect(screen.queryByText(/^\+\d+$/)).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Предыдущее фото' })).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /^Показать фото/ })).toHaveLength(n)
  })

  it('10 photos: every single one is reachable by paging forward (replaces the old "+N" ceiling, US-143)', async () => {
    const user = userEvent.setup()
    const all = photos(10)
    render(<CompanyPhotoGallery photos={all} companyName="Гвоздь" />)

    const next = screen.getByRole('button', { name: 'Следующее фото' })
    for (let i = 0; i < 9; i++) {
      await user.click(next)
    }
    // The last photo (by display order — cover first, then position) is now the active/eager slide.
    const activeImg = screen.getAllByRole('img', { hidden: true }).find((img) => img.getAttribute('loading') === 'eager')
    expect(activeImg).toHaveAttribute('src', all[9].url)
  })

  it('clicking a slide opens the lightbox; clicking an arrow or an indicator does not', async () => {
    const user = userEvent.setup()
    render(<CompanyPhotoGallery photos={photos(3)} companyName="Гвоздь" />)

    await user.click(screen.getByRole('button', { name: 'Следующее фото' }))
    expect(screen.queryByRole('button', { name: 'Закрыть' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Показать фото 3' }))
    expect(screen.queryByRole('button', { name: 'Закрыть' })).not.toBeInTheDocument()

    // Slide 1 is currently off-screen (`translateX`'d out of view) and, per the a11y fix below, out
    // of the accessibility tree too (its own `aria-hidden="true"` — not just ancestor CSS — zeroes
    // out its accessible name, so it must be reached by attribute, not by `getByRole(..., {name})`).
    await user.click(slideByLabel('Фото 1 из 3'))
    expect(screen.getByRole('button', { name: 'Закрыть' })).toBeInTheDocument()
  })

  it('only the active slide is reachable by keyboard/screen reader; the rest are tabIndex=-1 and aria-hidden', () => {
    render(<CompanyPhotoGallery photos={photos(3)} companyName="Гвоздь" />)
    const allSlides = ['Фото 1 из 3', 'Фото 2 из 3', 'Фото 3 из 3'].map(slideByLabel)
    expect(allSlides).toHaveLength(3)

    const active = screen.getByRole('button', { name: 'Фото 1 из 3' }) // findable by name: not aria-hidden
    expect(active).toHaveAttribute('tabIndex', '0')
    expect(active).toHaveAttribute('aria-hidden', 'false')

    const hidden = allSlides.filter((el) => el !== active)
    expect(hidden).toHaveLength(2)
    hidden.forEach((el) => {
      expect(el).toHaveAttribute('tabIndex', '-1')
      expect(el).toHaveAttribute('aria-hidden', 'true')
    })
  })

  it('single photo: the carousel root is not in the tab order (nothing to navigate to)', () => {
    render(<CompanyPhotoGallery photos={photos(1)} companyName="Гвоздь" />)
    expect(screen.getByRole('group', { name: 'Фотографии Гвоздь' })).toHaveAttribute('tabIndex', '-1')
  })

  it('opens a full-size lightbox on click and closes it on Escape', async () => {
    const user = userEvent.setup()
    render(<CompanyPhotoGallery photos={photos(1)} companyName="Гвоздь" />)

    await user.click(screen.getAllByRole('img')[0])
    expect(screen.getByRole('button', { name: 'Закрыть' })).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('button', { name: 'Закрыть' })).not.toBeInTheDocument()
  })

  it('navigates to the next photo in the lightbox with the right arrow key', async () => {
    const user = userEvent.setup()
    render(<CompanyPhotoGallery photos={photos(2)} companyName="Гвоздь" />)

    await user.click(screen.getByRole('button', { name: 'Фото 1 из 2' }))
    expect(lightboxImg()).toHaveAttribute('src', '/uploads/companies/p0.jpg')

    await user.keyboard('{ArrowRight}')
    expect(lightboxImg()).toHaveAttribute('src', '/uploads/companies/p1.jpg')
  })

  it('ArrowRight/ArrowLeft on the carousel root page slides and wrap at the ends', async () => {
    const user = userEvent.setup()
    render(<CompanyPhotoGallery photos={photos(3)} companyName="Гвоздь" />)
    const root = screen.getByRole('group', { name: 'Фотографии Гвоздь' })
    const active = () => screen.getAllByRole('img', { hidden: true }).find((img) => img.getAttribute('loading') === 'eager')!

    root.focus()
    expect(active()).toHaveAttribute('src', '/uploads/companies/p0.jpg')

    // wraps from the first slide back to the last
    await user.keyboard('{ArrowLeft}')
    expect(active()).toHaveAttribute('src', '/uploads/companies/p2.jpg')

    await user.keyboard('{ArrowRight}')
    expect(active()).toHaveAttribute('src', '/uploads/companies/p0.jpg')
  })

  it('has no autoplay: the active slide never changes on its own, even after 10s of fake timers (П9)', () => {
    vi.useFakeTimers()
    try {
      render(<CompanyPhotoGallery photos={photos(3)} companyName="Гвоздь" />)
      const active = () => screen.getAllByRole('img', { hidden: true }).find((img) => img.getAttribute('loading') === 'eager')!
      expect(active()).toHaveAttribute('src', '/uploads/companies/p0.jpg')
      vi.advanceTimersByTime(10_000)
      expect(active()).toHaveAttribute('src', '/uploads/companies/p0.jpg')
    } finally {
      vi.useRealTimers()
    }
  })

  it('uses eager loading only for the active slide, lazy for its neighbours, and the company name as alt text', () => {
    render(<CompanyPhotoGallery photos={photos(3)} companyName="Гвоздь" />)
    const images = screen.getAllByRole('img', { hidden: true })
    expect(images).toHaveLength(3) // active + both (wrap-aware) neighbours, all within the preload window
    const eager = images.filter((img) => img.getAttribute('loading') === 'eager')
    const lazy = images.filter((img) => img.getAttribute('loading') === 'lazy')
    expect(eager).toHaveLength(1)
    expect(lazy).toHaveLength(2)
    images.forEach((img) => expect(img).toHaveAttribute('alt', 'Гвоздь'))
  })

  it('slides carry a srcSet with the full-size image for larger viewports (R12)', () => {
    render(<CompanyPhotoGallery photos={photos(1)} companyName="Гвоздь" />)
    const img = screen.getAllByRole('img')[0]
    expect(img.getAttribute('srcSet')).toContain('/uploads/companies/p0.jpg 1600w')
    expect(img.getAttribute('srcSet')).toContain('/uploads/companies/p0-thumb.jpg 480w')
  })
})
