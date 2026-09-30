import { describe, it, expect, vi, beforeEach } from 'vitest'
import { Route, Routes, useLocation } from 'react-router-dom'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AxiosError } from 'axios'
import { DemoRoleButtons } from './DemoRoleButtons'
import { useAuthStore } from '../../store/authStore'
import { DemoProductProvider } from './DemoProductContext'
import { demoOrdersStatus, demoStatus, renderWithProviders } from './testUtils'

const getStatus = vi.fn()
const login = vi.fn()
vi.mock('../../api/demo', () => ({
  demoApi: { getStatus: (...a: unknown[]) => getStatus(...a), login: (...a: unknown[]) => login(...a) },
}))

function Where() {
  return <div data-testid="where">{useLocation().pathname}</div>
}
const ui = (
  <Routes>
    <Route
      path="*"
      element={
        <>
          <DemoRoleButtons />
          <Where />
        </>
      }
    />
  </Routes>
)

const authResponse = (roles: string[]) => ({
  token: 'demo-token',
  userId: 'u1',
  phone: '79000000001',
  email: null,
  firstName: 'Демо',
  lastName: 'Роль',
  roles,
  phoneVerified: true,
})

const plainError = (status: number, data: string) =>
  new AxiosError('x', String(status), undefined, undefined, {
    status,
    data,
    statusText: '',
    headers: {},
    config: {} as never,
  })

beforeEach(() => {
  getStatus.mockReset().mockResolvedValue(demoStatus())
  login.mockReset()
  useAuthStore.setState({ user: null, token: null })
})

describe('DemoRoleButtons (API_CONTRACT_CYCLE28.md §598)', () => {
  it('production: the block does not exist', async () => {
    getStatus.mockResolvedValue(null)
    renderWithProviders(ui)
    await waitFor(() => expect(getStatus).toHaveBeenCalled())
    expect(screen.queryByTestId('demo-role-buttons')).toBeNull()
  })

  it('demo: one button per role, labelled by the server', async () => {
    renderWithProviders(ui)
    expect(await screen.findByRole('button', { name: 'Войти как владелец салона' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Войти как мастер' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Войти как клиент' })).toBeInTheDocument()
  })

  it.each([
    ['Войти как владелец салона', 'owner', ['CompanyOwner'], '/cabinet'],
    ['Войти как мастер', 'master', ['Master'], '/my-bookings'],
    ['Войти как клиент', 'client', ['Client'], '/my-visits'],
  ])('%s: signs in under the role and opens its start screen', async (label, role, roles, path) => {
    login.mockResolvedValue(authResponse(roles))
    renderWithProviders(ui)
    await userEvent.setup().click(await screen.findByRole('button', { name: label }))
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent(path))
    expect(login).toHaveBeenCalledWith(role)
    expect(useAuthStore.getState().token).toBe('demo-token')
    expect(useAuthStore.getState().user?.roles).toEqual(roles)
  })

  it('409 "demo data not created yet": the server text is shown, nobody is signed in, buttons work again', async () => {
    login.mockRejectedValue(plainError(409, 'Демо-данные ещё не созданы. Зайдите чуть позже.'))
    renderWithProviders(ui)
    await userEvent.setup().click(await screen.findByRole('button', { name: 'Войти как мастер' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Демо-данные ещё не созданы. Зайдите чуть позже.')
    expect(useAuthStore.getState().token).toBeNull()
    expect(screen.getByRole('button', { name: 'Войти как мастер' })).toBeEnabled()
    expect(screen.getByTestId('where')).toHaveTextContent('/')
  })

  it('429: the rate-limit text from the server is shown', async () => {
    login.mockRejectedValue(plainError(429, 'Слишком много запросов'))
    renderWithProviders(ui)
    await userEvent.setup().click(await screen.findByRole('button', { name: 'Войти как клиент' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Слишком много запросов')
  })

  it('ezbook (no provider): asks for the salon roles and shows no shop buttons', async () => {
    renderWithProviders(ui)
    await screen.findByRole('button', { name: 'Войти как владелец салона' })
    expect(getStatus).toHaveBeenCalledWith('services')
    expect(screen.queryByRole('button', { name: 'Войти как покупатель' })).toBeNull()
  })
})

describe('DemoRoleButtons on goods (DemoProductProvider product="orders", API_CONTRACT_CYCLE35.md §35.22)', () => {
  const goodsUi = <DemoProductProvider product="orders">{ui}</DemoProductProvider>

  beforeEach(() => {
    getStatus.mockResolvedValue(demoOrdersStatus())
  })

  it('asks for the orders roles and renders exactly the three shop buttons (no salon ones)', async () => {
    renderWithProviders(goodsUi)
    expect(await screen.findByRole('button', { name: 'Войти как владелец магазина' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Войти как сотрудник магазина' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Войти как покупатель' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Войти как владелец салона' })).toBeNull()
    expect(getStatus).toHaveBeenCalledWith('orders')
  })

  it.each([
    ['Войти как владелец магазина', 'shop-owner', ['CompanyOwner'], '/cabinet'],
    ['Войти как сотрудник магазина', 'shop-staff', ['Master'], '/cabinet'],
    ['Войти как покупатель', 'shop-customer', ['Client'], '/orders'],
  ])('%s: signs in under the role and opens its start screen', async (label, role, roles, path) => {
    login.mockResolvedValue(authResponse(roles))
    renderWithProviders(goodsUi)
    await userEvent.setup().click(await screen.findByRole('button', { name: label }))
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent(path))
    expect(login).toHaveBeenCalledWith(role)
    expect(useAuthStore.getState().token).toBe('demo-token')
  })

  it('409 before the first reset after the release: the server text is shown, nobody is signed in', async () => {
    login.mockRejectedValue(plainError(409, 'Демо-данные ещё не созданы. Зайдите чуть позже.'))
    renderWithProviders(goodsUi)
    await userEvent.setup().click(await screen.findByRole('button', { name: 'Войти как владелец магазина' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Демо-данные ещё не созданы. Зайдите чуть позже.')
    expect(useAuthStore.getState().token).toBeNull()
  })

  it('production (status 404 → null): no block', async () => {
    getStatus.mockResolvedValue(null)
    renderWithProviders(goodsUi)
    await waitFor(() => expect(getStatus).toHaveBeenCalled())
    expect(screen.queryByTestId('demo-role-buttons')).toBeNull()
  })
})
