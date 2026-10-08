import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { LandingMedia } from './LandingMedia'

const cfg = { src: '/s.jpg', alt: 'Салон', width: 941, height: 1672, caption: 'Подпись' }

describe('LandingMedia', () => {
  it('keeps natural proportions: no fixed aspect ratio, no cover crop, intrinsic size set', () => {
    render(<LandingMedia config={cfg} />)
    const img = screen.getByAltText('Салон')
    expect(img.className).not.toMatch(/aspect-|object-cover/)
    expect(img.className).toContain('object-contain')
    expect(img.className).toMatch(/max-h-/)
    expect(img).toHaveAttribute('width', '941')
    expect(img).toHaveAttribute('height', '1672')
    expect(screen.getByText('Подпись')).toBeInTheDocument()
  })
  it('renders without caption', () => {
    render(<LandingMedia config={{ ...cfg, caption: undefined }} />)
    expect(screen.queryByText('Подпись')).toBeNull()
  })
})
