import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CompanyLayout } from './CompanyLayout'
import { useBathsCompany } from '../../cabinet/cabinetVertical'
import type { BathsCompanyManageDto } from '../../cabinet/types'

/** A failed request as axios reports it: a real Error that carries the response. */
const httpError = (status: number, data: unknown = '') => Object.assign(new Error('request failed'), { response: { status, data } })

const company = vi.fn()
vi.mock('../../api/bathsCabinet', () => ({ bathsCabinetApi: { company: (...a: unknown[]) => company(...a) } }))
vi.mock('../../cabinet/useBookingsRevision', () => ({ useBookingsRevision: () => undefined }))

const OWNER = ['ManageCompany', 'ViewBookings', 'ManageBookings', 'ManageServices', 'ViewSchedule', 'ViewCabinet']
const dto = (over: Partial<BathsCompanyManageDto> = {}): BathsCompanyManageDto =>
  ({
    id: 'c1',
    name: 'Сибирские бани',
    slug: 'sib',
    cityName: 'Кемерово',
    timeZoneId: 'Asia/Novokuznetsk',
    isActive: true,
    publicUrl: 'https://bani.ezbook.ru/sib',
    myRole: 'Owner',
    myPermissions: OWNER,
    gate: { accepting: true },
    plan: { isTrial: false, resourcesPublished: 1, warningLevel: 'None' },
    checklist: [],
    ...over,
  }) as BathsCompanyManageDto

/** What a screen under the layout reads from the outlet. */
function Probe() {
  const { company: c } = useBathsCompany()
  return <p>screen of {c.id}</p>
}

const renderLayout = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={['/cabinet/c1/probe']}>
        <Routes>
          <Route element={<CompanyLayout />}>
            <Route path="/cabinet/:companyId/probe" element={<Probe />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )

describe('CompanyLayout', () => {
  beforeEach(() => {
    company.mockReset()
  })

  it('hands the company to the screen below it through the outlet', async () => {
    company.mockResolvedValue(dto())
    renderLayout()
    expect(await screen.findByText('screen of c1')).toBeInTheDocument()
    expect(company).toHaveBeenCalledWith('c1')
  })

  it('shows the owner the gate, the plan and the unfinished steps, each step with its link', async () => {
    company.mockResolvedValue(
      dto({
        gate: { accepting: false, reasonCode: 'NoPlan', reasonText: 'Нет действующего тарифа' },
        plan: { isTrial: false, resourcesPublished: 1, warningLevel: 'NoPlan', text: 'Тариф не выбран. Гости не могут бронировать' },
        checklist: [
          { code: 'ProfileFilled', done: true, text: 'Заполните название, адрес и телефон для гостей' },
          { code: 'ProviderInfo', done: false, text: 'Заполните сведения об исполнителе' },
          { code: 'Plan', done: false, text: 'Выберите тариф или активируйте пробный период' },
        ],
      }),
    )
    renderLayout()
    expect(await screen.findByTestId('gate-banner')).toHaveTextContent('Нет действующего тарифа')
    expect(screen.getByTestId('plan-banner')).toHaveTextContent('Тариф не выбран')
    const list = screen.getByTestId('checklist')
    expect(list).toHaveTextContent('Гости не могут бронировать, потому что…')
    expect(list).not.toHaveTextContent('Заполните название')
    expect(screen.getByRole('link', { name: 'Указать исполнителя' })).toHaveAttribute('href', '/cabinet/c1/settings#provider')
    expect(screen.getByRole('link', { name: 'Выбрать тариф' })).toHaveAttribute('href', '/cabinet/subscription')
  })

  it('shows a bather the schedule tab only and none of the owner strip', async () => {
    company.mockResolvedValue(
      dto({
        myRole: 'Housekeeper',
        myPermissions: ['ViewSchedule'],
        gate: { accepting: false, reasonText: 'x' },
        checklist: [{ code: 'Plan', done: false, text: 'y' }],
      }),
    )
    renderLayout()
    expect(await screen.findByText('screen of c1')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Расписание' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Настройки' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'Ресурсы' })).toBeNull()
    expect(screen.queryByTestId('gate-banner')).toBeNull()
    expect(screen.queryByTestId('checklist')).toBeNull()
  })

  it('says «not found» for an unknown or foreign company (404 has no body)', async () => {
    company.mockRejectedValue(httpError(404))
    renderLayout()
    expect(await screen.findByText('Компания не найдена')).toBeInTheDocument()
  })

  it('offers a retry on a failure of the server', async () => {
    company.mockRejectedValue(httpError(500))
    renderLayout()
    expect(await screen.findByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })
})
