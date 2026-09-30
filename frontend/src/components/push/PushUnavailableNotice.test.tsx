import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { PushUnavailableNotice } from './PushUnavailableNotice'

// ARCHITECTURE_CYCLE33.md §33.8.2 — «install it» must come with «how»; the app name depends on the site.
describe('PushUnavailableNotice — cycle 33', () => {
  it('iPhone outside the installed app → explanation plus 4 numbered Home Screen steps with the app name', () => {
    render(<PushUnavailableNotice reason="ios-safari-not-installed" appName="Заказы" />)
    expect(screen.getByText(/только приложению «Заказы», добавленному на экран «Домой»/)).toBeInTheDocument()
    const steps = screen.getByRole('list', { name: /Как добавить «Заказы» на экран «Домой»/ })
    const items = steps.querySelectorAll('li')
    expect(items).toHaveLength(4)
    expect(items[0]).toHaveTextContent('«Поделиться»')
    expect(items[1]).toHaveTextContent('На экран «Домой»')
    expect(items[2]).toHaveTextContent('войдите заново')
    expect(items[3]).toHaveTextContent('«Профиль» → «Устройства и уведомления»')
    expect(screen.queryByText(/Достаточно сделать это/)).not.toBeInTheDocument()
  })

  it('showOneSiteHint adds the «one of the two sites» sentence', () => {
    render(<PushUnavailableNotice reason="ios-safari-not-installed" appName="Запись" showOneSiteHint />)
    expect(screen.getByText(/ezbook\.ru или goods\.ezbook\.ru/)).toBeInTheDocument()
  })

  it('other reasons show no install steps and expose reason for tests', () => {
    render(<PushUnavailableNotice reason="ios-version-too-old" appName="Запись" />)
    expect(screen.getByText(/iOS 16\.4 или новее/)).toBeInTheDocument()
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
    expect(screen.getByTestId('push-unavailable')).toHaveAttribute('data-reason', 'ios-version-too-old')
  })

  it('permission denied in the installed iPhone app points to iPhone Settings with the app name', () => {
    render(<PushUnavailableNotice reason="ios-permission-denied" appName="Запись" />)
    expect(screen.getByText(/Настройки айфона → Уведомления → «Запись»/)).toBeInTheDocument()
    expect(screen.queryByText(/адресной строке/)).not.toBeInTheDocument()
  })
})
