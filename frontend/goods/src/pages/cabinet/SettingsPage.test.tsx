import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { SettingsPage } from './SettingsPage'
import type { ShopManageDto } from '../../types'

const updateSettings = vi.fn()
// Rejections go through a plain function, not the spy: vitest's spy re-throws a recorded rejection on a derived
// promise, which surfaces as an unhandled error attributed to the test.
let failWith: unknown = null
vi.mock('../../api/shops', () => ({
  shopsApi: { updateSettings: (...a: unknown[]) => (failWith ? Promise.reject(failWith) : updateSettings(...a)), updateSeller: vi.fn() },
}))
vi.mock('@/api/companies', () => ({ companiesApi: { update: vi.fn(), uploadLogo: vi.fn() } }))
vi.mock('@/components/company/AddressVerifyField', () => ({ AddressVerifyField: () => <div>address-field</div> }))

const shop = (over: Partial<ShopManageDto> = {}): ShopManageDto =>
  ({
    id: 's1', name: 'Шаурма', slug: 'shaurma', timeZoneId: 'Asia/Barnaul', isActive: true, publicUrl: 'https://goods.ezbook.ru/shaurma', myRole: 'Owner',
    settings: { customerMode: 'Anyone', acceptanceMode: 'Manual', allowCustomerCancel: true, trackStock: false },
    seller: { isComplete: true, requiredFields: [] }, acceptingOrders: true, phoneVerificationAvailable: true, ...over,
  }) as ShopManageDto

function renderPage(s: ShopManageDto) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/cabinet/s1/settings']}>
        <Routes>
          <Route element={<Outlet context={{ shop: s, isOwner: true }} />}>
            <Route path="/cabinet/:shopId/settings" element={<SettingsPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  updateSettings.mockReset()
  failWith = null
})

describe('SettingsPage — ordering rules', () => {
  it('warns about MAX BEFORE saving the strict mode, and sends all four fields', async () => {
    updateSettings.mockResolvedValue(shop())
    const user = userEvent.setup()
    renderPage(shop())
    expect(screen.queryByRole('note')).toBeNull()
    await user.click(screen.getByRole('radio', { name: /Только с подтверждённым телефоном/ }))
    expect(screen.getByRole('note')).toHaveTextContent('Подтвердить номер можно только через MAX — покупатели без MAX не смогут заказать.')
    expect(updateSettings).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Сохранить правила' }))
    expect(updateSettings).toHaveBeenCalledWith('s1', { customerMode: 'VerifiedPhoneOnly', acceptanceMode: 'Manual', allowCustomerCancel: true, trackStock: false })
  })

  it('disables the strict mode when the confirmation subsystem is unavailable on the platform', () => {
    renderPage(shop({ phoneVerificationAvailable: false }))
    expect(screen.getByRole('radio', { name: /Только с подтверждённым телефоном/ })).toBeDisabled()
    expect(screen.getByText(/Подтверждение телефона сейчас недоступно на платформе/)).toBeInTheDocument()
  })

  it('keeps an already-enabled strict mode selectable even if verification went down', () => {
    renderPage(shop({ phoneVerificationAvailable: false, settings: { customerMode: 'VerifiedPhoneOnly', acceptanceMode: 'Manual', allowCustomerCancel: true, trackStock: false } as ShopManageDto['settings'] }))
    expect(screen.getByRole('radio', { name: /Только с подтверждённым телефоном/ })).toBeEnabled()
  })

  it('says why saving failed (server 409)', async () => {
    failWith = { response: { status: 409, data: { code: 'PhoneVerificationUnavailable', message: 'Подтверждение телефона сейчас недоступно на платформе — режим включить нельзя' } } }
    const user = userEvent.setup()
    renderPage(shop())
    await user.click(screen.getByRole('checkbox', { name: /Учитывать остатки/ }))
    await user.click(screen.getByRole('button', { name: 'Сохранить правила' }))
    expect(await screen.findByText('Подтверждение телефона сейчас недоступно на платформе — режим включить нельзя')).toBeInTheDocument()
  })
})
