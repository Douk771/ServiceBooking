import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SalonProfileSection } from './SalonProfileSection'
import type { City, Company } from '../../types'

const update = vi.fn()
const saveAddress = vi.fn()
vi.mock('../../api/companies', () => ({ companiesApi: { update: (...a: unknown[]) => update(...a), uploadLogo: vi.fn() } }))
vi.mock('../../api/companyAddress', () => ({ companyAddressApi: { saveAddress: (...a: unknown[]) => saveAddress(...a) } }))
vi.mock('../../components/company/PublicAddressNotice', () => ({
  PublicAddressNotice: ({ onConfirmed, onCancel }: { onConfirmed: () => void; onCancel: () => void }) => (
    <div role="dialog" aria-label="notice">
      <button onClick={onConfirmed}>notice-ok</button>
      <button onClick={onCancel}>notice-cancel</button>
    </div>
  ),
}))

const tomsk: City = { id: 2, name: 'Томск', region: 'Томская область', timeZoneId: 'Asia/Tomsk', utcOffsetMinutes: 420, label: 'Томск, Томская область' }
const moscow: City = { id: 3, name: 'Москва', region: 'Москва', timeZoneId: 'Europe/Moscow', utcOffsetMinutes: 180, label: 'Москва, Москва' }
vi.mock('../../components/ui/CityCombobox', () => ({
  CityCombobox: ({ label, value, onChange, error }: { label: string; value: City | null; onChange: (c: City | null) => void; error?: string }) => (
    <div>
      <span>{label}: {value?.name ?? 'нет'}</span>
      <button type="button" onClick={() => onChange(tomsk)}>pick-tomsk</button>
      <button type="button" onClick={() => onChange(moscow)}>pick-moscow</button>
      {error && <p role="alert">{error}</p>}
    </div>
  ),
}))

const salon = (over: Partial<Company> = {}): Company =>
  ({
    id: 'co1', name: 'Салон', slug: 'salon', description: 'Стрижки', phone: '79001234567', email: 'a@b.ru', address: 'Ленина, 1',
    cityId: 1, cityName: 'Барнаул', cityRegion: 'Алтайский край', timeZoneId: 'Asia/Barnaul', utcOffsetMinutes: 420, timeZoneIsManual: false,
    allowSelfBooking: true, ...over,
  }) as Company

function renderIt(c: Company) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const invalidate = vi.spyOn(qc, 'invalidateQueries')
  const ui = (x: Company) => (
    <QueryClientProvider client={qc}>
      <SalonProfileSection key={x.id} company={x} />
    </QueryClientProvider>
  )
  const r = render(ui(c))
  return { ...r, qc, invalidate }
}

const err = (status: number, data: string) => Promise.reject({ isAxiosError: true, response: { status, data } })
const save = (user: ReturnType<typeof userEvent.setup>) => user.click(screen.getByRole('button', { name: 'Сохранить' }))

beforeEach(() => {
  update.mockReset().mockResolvedValue({})
  saveAddress.mockReset().mockResolvedValue({ company: {} })
})

describe('SalonProfileSection — ARCHITECTURE_CYCLE32.md §32.5.2', () => {
  it('V32-01: one h2, labelled fields, texts of the salon', () => {
    renderIt(salon())
    expect(screen.getAllByRole('heading', { level: 2 })).toHaveLength(1)
    expect(screen.getByRole('heading', { name: 'Профиль салона' })).toBeInTheDocument()
    for (const l of ['Название *', 'Описание', 'Телефон для клиентов', 'Email для клиентов', 'Адрес', 'Яндекс Карты', '2ГИС'])
      expect(screen.getByLabelText(l)).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Адрес и карты' })).toBeInTheDocument()
    expect(screen.getByText('Виден на странице салона')).toBeInTheDocument()
    expect(screen.getByText('Адрес виден клиентам на странице салона')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Сохранить' })).toHaveLength(1)
  })

  it('V32-01: logo image has the salon alt', () => {
    renderIt(salon({ logoUrl: '/logo.png' }))
    expect(screen.getByAltText('Логотип салона')).toBeInTheDocument()
  })

  it('V32-02: untouched save sends exactly name/description/phone/email', async () => {
    const user = userEvent.setup()
    renderIt(salon())
    await save(user)
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update.mock.calls[0]).toEqual(['co1', { name: 'Салон', description: 'Стрижки', phone: '79001234567', email: 'a@b.ru' }])
    expect(saveAddress).not.toHaveBeenCalled()
  })

  it('V32-03: edited name and phone go in one body, phone as 7XXXXXXXXXX', async () => {
    const user = userEvent.setup()
    renderIt(salon())
    const name = screen.getByLabelText('Название *')
    await user.clear(name)
    await user.type(name, 'Ромашка')
    const phone = screen.getByLabelText('Телефон для клиентов')
    await user.clear(phone)
    await user.type(phone, '9005554433')
    await save(user)
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update.mock.calls[0][1]).toEqual({ name: 'Ромашка', description: 'Стрижки', phone: '79005554433', email: 'a@b.ru' })
  })

  it('V32-04: another offset asks first; cancel sends nothing, confirm sends cityId without timeZoneId', async () => {
    const user = userEvent.setup()
    renderIt(salon())
    await user.click(screen.getByText('pick-moscow'))
    await save(user)
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/Часовой пояс салона изменится: UTC\+7 → UTC\+3\./)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Отмена' }))
    expect(update).not.toHaveBeenCalled()
    await save(user)
    await user.click(await screen.findByRole('button', { name: 'Сменить город' }))
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    const body = update.mock.calls[0][1]
    expect(body.cityId).toBe(3)
    expect(body).not.toHaveProperty('timeZoneId')
  })

  it('V32-05: same offset -> no dialog, cityId in the body', async () => {
    const user = userEvent.setup()
    renderIt(salon())
    await user.click(screen.getByText('pick-tomsk'))
    await save(user)
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update.mock.calls[0][1].cityId).toBe(2)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('V32-06: changed address -> notice first, then update BEFORE saveAddress', async () => {
    const user = userEvent.setup()
    renderIt(salon())
    const addr = screen.getByLabelText('Адрес')
    await user.clear(addr)
    await user.type(addr, 'Мира, 2')
    await save(user)
    expect(update).not.toHaveBeenCalled()
    await user.click(await screen.findByText('notice-ok'))
    await waitFor(() => expect(saveAddress).toHaveBeenCalledWith('co1', 'Мира, 2'))
    expect(update.mock.invocationCallOrder[0]).toBeLessThan(saveAddress.mock.invocationCallOrder[0])
    expect(update.mock.calls[0][1]).not.toHaveProperty('address')
  })

  it('V32-07: address failure -> field error, "Остальные изменения сохранены", caches invalidated', async () => {
    saveAddress.mockImplementation(() => err(500, ''))
    const user = userEvent.setup()
    const { invalidate } = renderIt(salon())
    const addr = screen.getByLabelText('Адрес')
    await user.type(addr, '!')
    await save(user)
    await user.click(await screen.findByText('notice-ok'))
    expect(await screen.findByText('Не удалось сохранить адрес. Попробуйте ещё раз.')).toBeInTheDocument()
    expect(screen.getByText('Остальные изменения сохранены')).toBeInTheDocument()
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['my-companies'] })
  })

  it('V32-08: manual zone on -> dialog UTC+7 -> UTC+6 -> timeZoneId Asia/Omsk', async () => {
    const user = userEvent.setup()
    renderIt(salon())
    await user.click(screen.getByLabelText(/Указать часовой пояс вручную/))
    await user.type(screen.getByLabelText('Часовой пояс (IANA)'), 'Asia/Omsk')
    await save(user)
    expect(await screen.findByText(/UTC\+7 → UTC\+6/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Сменить город' }))
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update.mock.calls[0][1].timeZoneId).toBe('Asia/Omsk')
  })

  it('V32-08: manual zone off -> dialog "пояс города" -> timeZoneId null; untouched -> no keys', async () => {
    const user = userEvent.setup()
    renderIt(salon({ timeZoneId: 'Asia/Omsk', utcOffsetMinutes: 360, timeZoneIsManual: true }))
    expect(screen.getByText('Часовой пояс: Asia/Omsk — указан вручную')).toBeInTheDocument()
    await user.click(screen.getByLabelText(/Указать часовой пояс вручную/))
    expect(screen.getByText('Часовой пояс: как у города Барнаул, Алтайский край')).toBeInTheDocument()
    await save(user)
    expect(await screen.findByText(/→ пояс города Барнаул, Алтайский край/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Сменить город' }))
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update.mock.calls[0][1].timeZoneId).toBeNull()
  })

  it('V32-08: manual checkbox is disabled while no city is chosen; untouched manual salon sends no zone keys', async () => {
    const user = userEvent.setup()
    const { unmount } = renderIt(salon({ cityId: null, cityName: null, cityRegion: null }))
    expect(screen.getByLabelText(/Указать часовой пояс вручную/)).toBeDisabled()
    unmount()
    renderIt(salon({ timeZoneId: 'Asia/Omsk', utcOffsetMinutes: 360, timeZoneIsManual: true }))
    await save(user)
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update.mock.calls[0][1]).not.toHaveProperty('timeZoneId')
    expect(update.mock.calls[0][1]).not.toHaveProperty('cityId')
  })

  it('V32-09: empty name -> error at the field, no request', async () => {
    const user = userEvent.setup()
    renderIt(salon())
    await user.clear(screen.getByLabelText('Название *'))
    await save(user)
    expect(await screen.findByText('Введите название салона')).toBeInTheDocument()
    expect(update).not.toHaveBeenCalled()
  })

  it.each([
    ['Город не найден', 'city'],
    ['Неизвестный часовой пояс', 'zone'],
    ['Ждём ссылку на Яндекс Карты — например, https://yandex.ru/maps/org/1', 'yandex'],
  ])('V32-10: 400 "%s" goes to its field', async (text, where) => {
    update.mockImplementation(() => err(400, text))
    const user = userEvent.setup()
    renderIt(salon())
    if (where === 'city') await user.click(screen.getByText('pick-tomsk'))
    if (where === 'zone') {
      await user.click(screen.getByLabelText(/Указать часовой пояс вручную/))
      await user.type(screen.getByLabelText('Часовой пояс (IANA)'), 'Asia/Nowhere')
    }
    if (where === 'yandex') await user.type(screen.getByLabelText('Яндекс Карты'), 'x')
    await save(user)
    await waitFor(() => expect(update).toHaveBeenCalled())
    const field =
      where === 'city' ? screen.getByRole('alert') : where === 'zone' ? screen.getByLabelText('Часовой пояс (IANA)') : screen.getByLabelText('Яндекс Карты')
    if (where === 'city') expect(field).toHaveTextContent(text)
    else await waitFor(() => expect(field).toHaveAccessibleDescription(text))
  })

  it('V32-10: another 400 goes under the form with the mapper text', async () => {
    update.mockImplementation(() => err(400, 'что-то ещё'))
    const user = userEvent.setup()
    renderIt(salon())
    await save(user)
    expect(await screen.findByRole('alert')).toHaveTextContent('Проверьте введённые данные — сервер их не принял.')
  })

  it('V32-11: typed text survives a re-read of my-companies, Save stays enabled', async () => {
    const user = userEvent.setup()
    const { qc } = renderIt(salon())
    const name = screen.getByLabelText('Название *')
    await user.clear(name)
    await user.type(name, 'Новое название')
    qc.setQueryData(['my-companies'], [salon({ allowSelfBooking: false })])
    expect(name).toHaveValue('Новое название')
    expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled()
  })

  it('V32-12: legacy phone hint; phone sent byte for byte; hint gone after editing', async () => {
    const legacy = '8 (3852) 12-34-56 доб. 5'
    const user = userEvent.setup()
    renderIt(salon({ phone: legacy }))
    const hint = screen.getByText(/Сейчас сохранён номер «8 \(3852\) 12-34-56 доб\. 5»/)
    expect(screen.getByLabelText('Телефон для клиентов')).toHaveAttribute('aria-describedby', hint.id)
    const name = screen.getByLabelText('Название *')
    await user.type(name, '!')
    await save(user)
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update.mock.calls[0][1].phone).toBe(legacy)
    await user.type(screen.getByLabelText('Телефон для клиентов'), '9')
    expect(screen.queryByText(/Сейчас сохранён номер/)).not.toBeInTheDocument()
  })

  it('V32-12: canonical phone shows no hint', () => {
    renderIt(salon())
    expect(screen.queryByText(/Сейчас сохранён номер/)).not.toBeInTheDocument()
  })

  it('V32-13: success shows the status and invalidates my-companies and company', async () => {
    const user = userEvent.setup()
    const { invalidate } = renderIt(salon())
    await save(user)
    expect(await screen.findByRole('status')).toHaveTextContent('Сохранено')
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['my-companies'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['company'] })
  })
})
