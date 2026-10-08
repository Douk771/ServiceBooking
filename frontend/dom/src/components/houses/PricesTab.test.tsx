import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PricesTab } from './PricesTab'
import { HouseTabProvider } from './houseContext'
import { houseManageFixture, httpError } from '../../test/fixtures'
import type { HouseManageDto } from '../../types'

const api = vi.hoisted(() => ({ pricePeriods: vi.fn(), createPricePeriod: vi.fn(), updatePricePeriod: vi.fn(), deletePricePeriod: vi.fn(), updatePricing: vi.fn() }))
vi.mock('../../api/staysHouses', () => ({ staysHousesApi: api }))

function renderTab(house: HouseManageDto, canManage = true) {
  const setHouse = vi.fn()
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={qc}>
      <HouseTabProvider value={{ companyId: 'co-1', house, setHouse, canManage, canEditContent: true, timeZoneId: 'Asia/Novokuznetsk' }}>
        <PricesTab />
      </HouseTabProvider>
    </QueryClientProvider>,
  )
  return { setHouse }
}

const byDates = (over: Partial<HouseManageDto> = {}) => houseManageFixture({ priceMode: 'ByDates', constantPriceRub: null, ...over })

beforeEach(() => {
  Object.values(api).forEach((f) => f.mockReset())
  api.pricePeriods.mockResolvedValue([])
})

describe('PricesTab', () => {
  it('constant mode: validates the price (1…1 000 000) and saves mode + price', async () => {
    api.updatePricing.mockResolvedValue(houseManageFixture({ constantPriceRub: 6500 }))
    const { setHouse } = renderTab(houseManageFixture())
    const price = screen.getByLabelText('Цена за ночь')
    fireEvent.change(price, { target: { value: '0' } })
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(await screen.findByText('Цена — от 1 до 1 000 000 ₽')).toBeInTheDocument()
    expect(api.updatePricing).not.toHaveBeenCalled()
    fireEvent.change(price, { target: { value: '6500' } })
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(api.updatePricing).toHaveBeenCalledWith('co-1', 'house-1', { mode: 'Constant', constantPriceRub: 6500 }))
    await waitFor(() => expect(setHouse).toHaveBeenCalled())
  })

  it('by-dates mode: lists periods, names the dates with no price in words, and the calendar marks them', async () => {
    api.pricePeriods.mockResolvedValue([{ id: 'p1', startDate: '2027-12-20', endDate: '2028-01-10', priceRub: 6000 }])
    renderTab(byDates({ uncoveredDates: [{ startDate: '2027-02-01', endDate: '2027-02-05' }] }))
    expect(await screen.findByText(/6\s000 ₽ за ночь/)).toBeInTheDocument()
    expect(screen.getByTestId('uncovered-warning')).toHaveTextContent('Без цены останутся 5 дн.')
    expect(screen.getByTestId('uncovered-warning')).toHaveTextContent('01.02.2027–05.02.2027')
  })

  it('an empty by-dates house says it cannot be booked yet', async () => {
    renderTab(byDates())
    expect(await screen.findByText(/Периодов пока нет/)).toBeInTheDocument()
  })

  it('adding a period: dates and price are checked, overlap comes back as the server sentence', async () => {
    api.createPricePeriod.mockRejectedValue(httpError(409, { code: 'PricePeriodOverlap', message: 'Период пересекается с 20.12.2027–10.01.2028 (6 000 ₽)' }))
    renderTab(byDates())
    fireEvent.click(await screen.findByRole('button', { name: 'Добавить период' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dialog).findByText('Укажите дату начала')).toBeInTheDocument()
    expect(api.createPricePeriod).not.toHaveBeenCalled()

    fireEvent.change(within(dialog).getByLabelText('С даты'), { target: { value: '2027-12-25' } })
    fireEvent.change(within(dialog).getByLabelText('Цена за ночь'), { target: { value: '9000' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(api.createPricePeriod).toHaveBeenCalledWith('co-1', 'house-1', { startDate: '2027-12-25', endDate: '2027-12-25', priceRub: 9000 }))
    expect(await within(dialog).findByText('Период пересекается с 20.12.2027–10.01.2028 (6 000 ₽)')).toBeInTheDocument()
  })

  it('deleting a period asks first', async () => {
    api.pricePeriods.mockResolvedValue([{ id: 'p1', startDate: '2027-12-20', endDate: '2028-01-10', priceRub: 6000 }])
    api.deletePricePeriod.mockResolvedValue(undefined)
    renderTab(byDates())
    fireEvent.click(await screen.findByRole('button', { name: 'Удалить' }))
    const dialog = await screen.findByRole('dialog')
    expect(api.deletePricePeriod).not.toHaveBeenCalled()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Удалить' }))
    await waitFor(() => expect(api.deletePricePeriod).toHaveBeenCalledWith('co-1', 'house-1', 'p1'))
  })

  it('a manager without ManageHouses does not get the prices', () => {
    renderTab(houseManageFixture(), false)
    expect(screen.getByText('Цены дома меняет владелец.')).toBeInTheDocument()
    expect(api.pricePeriods).not.toHaveBeenCalled()
  })

  it('a failed period list offers a retry', async () => {
    api.pricePeriods.mockRejectedValue(httpError(500, ''))
    renderTab(byDates())
    expect(await screen.findByText('Сервер временно недоступен. Попробуйте позже.')).toBeInTheDocument()
  })
})
