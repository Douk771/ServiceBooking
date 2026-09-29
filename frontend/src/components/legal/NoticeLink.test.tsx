import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { NoticeLink, NoticeLinkProvider } from './NoticeLink'

describe('NoticeLink — a platform notice linkUrl on ezbook vs goods', () => {
  it('is an in-app link by default (ezbook)', () => {
    render(
      <MemoryRouter>
        <NoticeLink to="/billing">Подробнее</NoticeLink>
      </MemoryRouter>,
    )
    expect(screen.getByRole('link', { name: 'Подробнее' })).toHaveAttribute('href', '/billing')
  })

  it('points at the ezbook origin on goods', () => {
    render(
      <MemoryRouter>
        <NoticeLinkProvider base={{ kind: 'external', origin: 'https://ezbook.ru' }}>
          <NoticeLink to="/billing">Подробнее</NoticeLink>
        </NoticeLinkProvider>
      </MemoryRouter>,
    )
    expect(screen.getByRole('link', { name: 'Подробнее' })).toHaveAttribute('href', 'https://ezbook.ru/billing')
  })

  it('renders nothing on goods while the ezbook origin is unknown', () => {
    render(
      <MemoryRouter>
        <NoticeLinkProvider base={{ kind: 'external', origin: null }}>
          <NoticeLink to="/billing">Подробнее</NoticeLink>
        </NoticeLinkProvider>
      </MemoryRouter>,
    )
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })
})
