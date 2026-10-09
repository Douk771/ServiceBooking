import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CreateCompanyPage } from './CreateCompanyPage'

const create = vi.fn()
const trialState = vi.fn()
const slugCheck = vi.fn()
vi.mock('../../api/bathsCompanies', () => ({
  bathsCompaniesApi: { create: (...a: unknown[]) => create(...a), trialState: () => trialState(), slugCheck: (...a: unknown[]) => slugCheck(...a) },
}))
vi.mock('@/api/legal', () => ({ legalApi: { getManifest: () => Promise.resolve({ documents: [{ type: 'TermsOwner', version: 'v7' }] }) } }))
// The text under the phone is a legal microcopy: not the subject of this test.
vi.mock('@/components/slots/ui/SlotNotice', () => ({ SlotNotice: ({ textKey }: { textKey: string }) => <p>notice:{textKey}</p> }))

const renderPage = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <CreateCompanyPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )

describe('CreateCompanyPage', () => {
  beforeEach(() => {
    create.mockReset()
    trialState.mockReset()
    slugCheck.mockReset()
    trialState.mockResolvedValue({ offered: false, eligible: false, durationDays: 14 })
    slugCheck.mockResolvedValue({ suggested: 'sibirskie-bani', available: true })
  })

  it('keeps the button off until the agreement is accepted', async () => {
    renderPage()
    const button = await screen.findByRole('button', { name: 'Создать компанию' })
    expect(button).toBeDisabled()
    await userEvent.click(screen.getByRole('checkbox', { name: /Я принимаю/ }))
    expect(button).toBeEnabled()
  })

  it('asks for the city and does not create the company without one', async () => {
    renderPage()
    await userEvent.type(await screen.findByLabelText('Название комплекса *'), 'Сибирские бани')
    await userEvent.click(screen.getByRole('checkbox', { name: /Я принимаю/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Создать компанию' }))
    expect(await screen.findByText('Укажите город комплекса')).toBeInTheDocument()
    expect(create).not.toHaveBeenCalled()
  })

  it('says honestly that a missing city has to be reported, and shows the legal notice by the phone', async () => {
    renderPage()
    expect(await screen.findByText(/Вашего города пока нет в списке — напишите в поддержку/)).toBeInTheDocument()
    expect(screen.getByText('notice:BathPublicContactsNotice')).toBeInTheDocument()
  })

  it('shows the trial block only when the trial is offered', async () => {
    renderPage()
    await screen.findByLabelText('Название комплекса *')
    expect(screen.queryByTestId('trial-block')).toBeNull()
  })

  it('shows the conditions of the trial before the owner agrees to start it', async () => {
    trialState.mockResolvedValue({ offered: true, eligible: true, durationDays: 14, termsVersion: 'baths-1', termsText: 'Условия пробного периода «Бани»' })
    renderPage()
    expect(await screen.findByTestId('trial-block')).toHaveTextContent('Условия пробного периода «Бани»')
    expect(screen.getByRole('checkbox', { name: /Начать пробный период/ })).toBeChecked()
  })
})
