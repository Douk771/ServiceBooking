import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { PushUnavailableNotice } from './PushUnavailableNotice'

// ARCHITECTURE_CYCLE19.md §363 (US-19-01) — "install it" must come with "how".
describe('PushUnavailableNotice — cycle 19', () => {
  it('CY19-13 iPhone outside the installed app → explanation plus numbered Home Screen steps', () => {
    render(<PushUnavailableNotice reason="ios-safari-not-installed" />)
    expect(screen.getByText(/только приложению EZBOOK, добавленному на экран «Домой»/)).toBeInTheDocument()
    const steps = screen.getByRole('list', { name: /Как добавить EZBOOK на экран «Домой»/ })
    const items = steps.querySelectorAll('li')
    expect(items).toHaveLength(4)
    expect(items[0]).toHaveTextContent('«Поделиться»')
    expect(items[1]).toHaveTextContent('На экран «Домой»')
    // Separate storage in the installed app: without this line the master lands on a login form and
    // assumes the install failed.
    expect(items[2]).toHaveTextContent('войдите заново')
  })

  it('CY19-14 other reasons show no install steps', () => {
    render(<PushUnavailableNotice reason="ios-version-too-old" />)
    expect(screen.getByText(/iOS 16\.4 или новее/)).toBeInTheDocument()
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
  })

  it('CY19-15 permission denied in the installed iPhone app points to iPhone Settings, not the address bar', () => {
    render(<PushUnavailableNotice reason="ios-permission-denied" />)
    expect(screen.getByText(/Настройки айфона → Уведомления → EZBOOK/)).toBeInTheDocument()
    expect(screen.queryByText(/адресной строке/)).not.toBeInTheDocument()
  })
})
