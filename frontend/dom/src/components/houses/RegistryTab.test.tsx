import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RegistryTab } from './RegistryTab'
import { HouseTabProvider } from './houseContext'
import { houseManageFixture, httpError } from '../../test/fixtures'
import type { HouseManageDto } from '../../types'

const api = vi.hoisted(() => ({ updateRegistry: vi.fn(), publish: vi.fn(), unpublish: vi.fn(), archive: vi.fn(), remove: vi.fn() }))
vi.mock('../../api/staysHouses', () => ({ staysHousesApi: api }))
vi.mock('../../api/legalTexts', () => ({ stayLegalTextsApi: { get: () => Promise.reject(httpError(404, '')) } }))

function renderTab(house: HouseManageDto, canManage = true) {
  const setHouse = vi.fn()
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <HouseTabProvider value={{ companyId: 'co-1', house, setHouse, canManage, canEditContent: true, timeZoneId: 'Asia/Novokuznetsk' }}>
          <RegistryTab />
        </HouseTabProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { setHouse }
}

beforeEach(() => Object.values(api).forEach((f) => f.mockReset()))

describe('RegistryTab — ЮР-2: publication under the owner’s attestation', () => {
  it('a house without a registry number can be published: the dialog shows the server text, needs the tick, and sends the attestation with the version', async () => {
    api.publish.mockResolvedValue(houseManageFixture({ isPublished: true }))
    const { setHouse } = renderTab(houseManageFixture({ publishProblems: ['AttestationRequired'] }))
    fireEvent.click(screen.getByRole('button', { name: 'Опубликовать' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Вы отвечаете за достоверность сведений о доме.')).toBeInTheDocument()
    expect(within(dialog).getByText(/без номера/)).toBeInTheDocument()
    const go = within(dialog).getByRole('button', { name: 'Подтверждаю и публикую' })
    expect(go).toBeDisabled()
    fireEvent.click(within(dialog).getByRole('checkbox'))
    expect(go).toBeEnabled()
    fireEvent.click(go)
    await waitFor(() => expect(api.publish).toHaveBeenCalledWith('co-1', 'house-1', { attestation: { accepted: true, noticeVersion: 'v1' } }))
    await waitFor(() => expect(setHouse).toHaveBeenCalled())
  })

  it('what stops the publication is listed in the server words and the button is off', () => {
    renderTab(houseManageFixture({ publishProblems: ['NoPrice', 'ObjectKindRequired', 'AttestationRequired'] }))
    const list = screen.getByTestId('publish-blockers')
    expect(list).toHaveTextContent('Задайте цену: постоянную или хотя бы один период на будущие даты')
    expect(list).toHaveTextContent('Укажите вид объекта')
    expect(list).not.toHaveTextContent('Подтвердите сведения о доме') // the dialog itself closes that one
    expect(screen.getByRole('button', { name: 'Опубликовать' })).toBeDisabled()
  })

  it('the tariff limit (402) says so in the server words and offers the tariff page', async () => {
    api.publish.mockRejectedValue(httpError(402, 'Тариф «Старт» позволяет опубликовать 1 дом. Снимите дом с публикации или смените тариф.'))
    renderTab(houseManageFixture())
    fireEvent.click(screen.getByRole('button', { name: 'Опубликовать' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('checkbox'))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Подтверждаю и публикую' }))
    expect(await screen.findByText(/позволяет опубликовать 1 дом/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Выбрать тариф' })).toHaveAttribute('href', '/cabinet/subscription')
  })

  it('a published house: changing the registry asks for a new attestation in the same request', async () => {
    api.updateRegistry.mockResolvedValue(houseManageFixture({ isPublished: true }))
    renderTab(houseManageFixture({ isPublished: true, registry: { objectKind: 'GuestHouse', registryNumber: 'KEM-2025-1', registryUrl: null, lastAttestation: null } }))
    fireEvent.change(screen.getByLabelText('Номер в реестре'), { target: { value: 'KEM-2025-2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    const dialog = await screen.findByRole('dialog')
    expect(api.updateRegistry).not.toHaveBeenCalled()
    fireEvent.click(within(dialog).getByRole('checkbox'))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Подтверждаю и сохраняю' }))
    await waitFor(() => expect(api.updateRegistry).toHaveBeenCalledTimes(1))
    expect(api.updateRegistry.mock.calls[0][2]).toMatchObject({ objectKind: 'GuestHouse', registryNumber: 'KEM-2025-2', attestation: { accepted: true, noticeVersion: 'v1' } })
  })

  it('an unpublished house saves the registry without the attestation', async () => {
    api.updateRegistry.mockResolvedValue(houseManageFixture())
    renderTab(houseManageFixture())
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(api.updateRegistry).toHaveBeenCalledTimes(1))
    expect(api.updateRegistry.mock.calls[0][2].attestation).toBeNull()
  })

  it('validates the number and the link before the request', async () => {
    renderTab(houseManageFixture())
    fireEvent.change(screen.getByLabelText('Номер в реестре'), { target: { value: 'ab' } })
    fireEvent.change(screen.getByLabelText('Ссылка на запись в реестре'), { target: { value: 'http://x.ru' } })
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(await screen.findByText(/5–32 символа/)).toBeInTheDocument()
    expect(screen.getByText('Ссылка на запись в реестре должна начинаться с https://')).toBeInTheDocument()
    expect(api.updateRegistry).not.toHaveBeenCalled()
  })

  it('shows the last attestation: who, when, what', () => {
    renderTab(
      houseManageFixture({
        registry: { objectKind: 'GuestHouse', registryNumber: 'KEM-1', registryUrl: null, lastAttestation: { attestedAtUtc: '2027-01-02T10:00:00Z', attestedByName: 'Иван Иванов', objectKind: 'GuestHouse', registryNumber: 'KEM-1' } },
      }),
    )
    expect(screen.getByTestId('last-attestation')).toHaveTextContent('Иван Иванов')
    expect(screen.getByTestId('last-attestation')).toHaveTextContent('Гостевой дом, № KEM-1')
  })

  it('deleting a house with bookings shows the server reason (409) instead of deleting', async () => {
    api.remove.mockRejectedValue(httpError(409, { code: 'HouseHasBookings', message: 'У дома есть брони — его можно только архивировать' }))
    renderTab(houseManageFixture({ hasBookings: true }))
    fireEvent.click(screen.getByRole('button', { name: 'Удалить дом' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Удалить' }))
    expect(await screen.findByText('У дома есть брони — его можно только архивировать')).toBeInTheDocument()
  })

  it('a manager (no ManageHouses) cannot reach the registry and publication', () => {
    renderTab(houseManageFixture(), false)
    expect(screen.getByText(/ведёт владелец/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Опубликовать' })).not.toBeInTheDocument()
  })
})
