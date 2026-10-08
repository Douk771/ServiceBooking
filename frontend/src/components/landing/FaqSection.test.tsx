import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { FaqSection } from './FaqSection'
import { faqItems } from './testConfig'
import type { FaqItems } from './types'

const r = (items: FaqItems = faqItems) => render(<MemoryRouter><FaqSection items={items} /></MemoryRouter>)

describe('FaqSection (T38-06)', () => {
  it('starts collapsed, buttons live inside h3', () => {
    const { container } = r()
    const buttons = screen.getAllByRole('button')
    expect(buttons).toHaveLength(6)
    buttons.forEach((b) => {
      expect(b).toHaveAttribute('aria-expanded', 'false')
      expect(b.parentElement!.tagName).toBe('H3')
      expect(container.querySelector(`#${CSS.escape(b.getAttribute('aria-controls')!)}`)).toHaveAttribute('hidden')
    })
  })
  it('Tab reaches every button; Enter and Space toggle', async () => {
    const user = userEvent.setup()
    r()
    const buttons = screen.getAllByRole('button')
    for (const b of buttons) {
      await user.tab()
      expect(b).toHaveFocus()
    }
    buttons[0].focus()
    await user.keyboard('{Enter}')
    expect(buttons[0]).toHaveAttribute('aria-expanded', 'true')
    await user.keyboard('{Enter}')
    expect(buttons[0]).toHaveAttribute('aria-expanded', 'false')
    await user.keyboard(' ')
    expect(buttons[0]).toHaveAttribute('aria-expanded', 'true')
  })
  it('keeps several items open and syncs hidden', async () => {
    const user = userEvent.setup()
    const { container } = r()
    const [b0, b1] = screen.getAllByRole('button')
    await user.click(b0)
    await user.click(b1)
    for (const b of [b0, b1]) {
      expect(b).toHaveAttribute('aria-expanded', 'true')
      expect(container.querySelector(`#${CSS.escape(b.getAttribute('aria-controls')!)}`)).not.toHaveAttribute('hidden')
    }
  })
  it('prints HTML literally', () => {
    const items = [{ question: 'Q', answer: '<b>x</b>' }, ...faqItems.slice(1)] as unknown as FaqItems
    const { container } = r(items)
    expect(container.querySelector('b')).toBeNull()
    expect(container.textContent).toContain('<b>x</b>')
  })
  it('renders paragraphs and the optional link', () => {
    r()
    expect(screen.getByText('A6a')).toBeInTheDocument()
    expect(screen.getByText('A6b')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Тарифы и цены', hidden: true })).toHaveAttribute('href', '/pricing')
  })
})
