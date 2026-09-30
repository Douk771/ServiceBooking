import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { CompanyLogoMark } from './CompanyLogoMark'

describe('CompanyLogoMark (V29-31)', () => {
  it('renders image with alt="" and dimensions; lazy only for catalog', () => {
    const { rerender } = render(<CompanyLogoMark name="Кофе" logoUrl="/a.jpg" size="catalog" />)
    let img = screen.getByTestId('company-logo-img')
    expect(img).toHaveAttribute('alt', '')
    expect(img).toHaveAttribute('width', '56')
    expect(img).toHaveAttribute('height', '56')
    expect(img).toHaveAttribute('loading', 'lazy')
    rerender(<CompanyLogoMark name="Кофе" logoUrl="/a.jpg" size="card" />)
    img = screen.getByTestId('company-logo-img')
    expect(img).toHaveAttribute('width', '64')
    expect(img).not.toHaveAttribute('loading')
  })
  it('letter without logoUrl', () => {
    render(<CompanyLogoMark name="«Ромашка»" size="card" />)
    expect(screen.getByTestId('company-logo-initial')).toHaveTextContent('Р')
  })
  it('letter after image error; new logoUrl retries', () => {
    const { rerender } = render(<CompanyLogoMark name="Кофе" logoUrl="/a.jpg" size="catalog" />)
    fireEvent.error(screen.getByTestId('company-logo-img'))
    expect(screen.getByTestId('company-logo-initial')).toHaveTextContent('К')
    rerender(<CompanyLogoMark name="Кофе" logoUrl="/b.jpg" size="catalog" />)
    expect(screen.getByTestId('company-logo-img')).toHaveAttribute('src', '/b.jpg')
  })
})
