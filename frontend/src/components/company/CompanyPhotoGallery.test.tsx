import { describe, it, expect } from 'vitest'
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

describe('CompanyPhotoGallery — API_CONTRACT_CYCLE10.md §125/§129', () => {
  it('renders a plain placeholder, no technical text, when there are no photos', () => {
    render(<CompanyPhotoGallery photos={[]} companyName="Гвоздь" />)
    expect(screen.queryByText('Фото: компания')).not.toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('uses lazy loading and the company name as alt text on every thumbnail', () => {
    render(
      <CompanyPhotoGallery
        photos={[photo({ id: 'p1', isCover: true }), photo({ id: 'p2', position: 1, isCover: false })]}
        companyName="Гвоздь"
      />,
    )
    const images = screen.getAllByRole('img')
    expect(images).toHaveLength(2)
    images.forEach((img) => {
      expect(img).toHaveAttribute('loading', 'lazy')
      expect(img).toHaveAttribute('alt', 'Гвоздь')
    })
  })

  it('opens a full-size lightbox on click and closes it on Escape', async () => {
    const user = userEvent.setup()
    render(<CompanyPhotoGallery photos={[photo()]} companyName="Гвоздь" />)

    await user.click(screen.getAllByRole('img')[0])
    expect(screen.getByRole('button', { name: 'Закрыть' })).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('button', { name: 'Закрыть' })).not.toBeInTheDocument()
  })

  it('navigates to the next photo with the right arrow key', async () => {
    const user = userEvent.setup()
    const photos = [photo({ id: 'p1' }), photo({ id: 'p2', position: 1, isCover: false, url: '/uploads/companies/p2.jpg' })]
    render(<CompanyPhotoGallery photos={photos} companyName="Гвоздь" />)

    await user.click(screen.getAllByRole('img')[0])
    const lightboxImg = () => screen.getByRole('button', { name: 'Закрыть' }).parentElement!.querySelector('img')!
    expect(lightboxImg()).toHaveAttribute('src', '/uploads/companies/p1.jpg')

    await user.keyboard('{ArrowRight}')
    expect(lightboxImg()).toHaveAttribute('src', '/uploads/companies/p2.jpg')
  })
})
