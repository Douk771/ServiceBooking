import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { MessengerAddonLines, type MessengerAddonDto, type MessengerAddonsNoteDto } from './MessengerAddonLines'

const MAX: MessengerAddonDto = { transport: 'Max', label: 'MAX', pricePerMonth: 490, text: '+ MAX 490 ₽/мес', footnote: null }
const WA: MessengerAddonDto = {
  transport: 'WhatsApp',
  label: 'WhatsApp',
  pricePerMonth: 390,
  text: '+ WhatsApp 390 ₽/мес',
  footnote: 'Доступ к WhatsApp в России ограничен: сообщения могут не доходить',
}
const NOTE: MessengerAddonsNoteDto = { conditionsUrl: '/offer-channel', conditionsLabel: 'Условия', taxNote: 'Цены указаны с учётом налогов' }

const renderLines = (addons: MessengerAddonDto[] | undefined, note: MessengerAddonsNoteDto | null | undefined) =>
  render(
    <MemoryRouter>
      <MessengerAddonLines addons={addons} note={note} />
    </MemoryRouter>,
  )

describe('MessengerAddonLines (Т40-L-11)', () => {
  it('only MAX open: one line, no footnote, no WhatsApp', () => {
    renderLines([MAX], NOTE)
    expect(screen.getByText('+ MAX 490 ₽/мес')).toBeInTheDocument()
    expect(screen.queryByText(/WhatsApp/)).toBeNull()
    expect(screen.queryByText(/ограничен/)).toBeNull()
  })

  it('both open: two separate lines, footnote under WhatsApp only, no word «от»', () => {
    renderLines([WA, MAX], NOTE)
    const wa = screen.getByTestId('messenger-addon-WhatsApp')
    expect(wa).toHaveTextContent('+ WhatsApp 390 ₽/мес')
    expect(wa).toHaveTextContent('сообщения могут не доходить')
    expect(screen.getByTestId('messenger-addon-MAX')).not.toHaveTextContent('ограничен')
    expect(screen.getByTestId('messenger-addon-lines').textContent).not.toMatch(/(^|\s)от\s/i)
  })

  it('links «Условия» to the server url and prints the tax note', () => {
    renderLines([MAX], NOTE)
    expect(screen.getByRole('link', { name: 'Условия' })).toHaveAttribute('href', '/offer-channel')
    expect(screen.getByText(/Цены указаны с учётом налогов/)).toBeInTheDocument()
  })

  it.each([[[]], [undefined]])('renders nothing for an empty/absent list (%j)', (addons) => {
    const { container } = renderLines(addons as MessengerAddonDto[] | undefined, NOTE)
    expect(container).toBeEmptyDOMElement()
  })
})
