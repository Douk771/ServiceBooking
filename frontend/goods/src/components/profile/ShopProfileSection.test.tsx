import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ShopProfileSection } from './ShopProfileSection'
import type { ShopManageDto } from '../../types'
import type { City } from '@/types'

const update = vi.fn()
const saveAddress = vi.fn()
vi.mock('@/api/companies', () => ({ companiesApi: { update: (...a: unknown[]) => update(...a), uploadLogo: vi.fn() } }))
vi.mock('@/api/companyAddress', () => ({ companyAddressApi: { saveAddress: (...a: unknown[]) => saveAddress(...a) } }))
vi.mock('@/components/company/PublicAddressNotice', () => ({
  PublicAddressNotice: ({ onConfirmed, onCancel }: { onConfirmed: () => void; onCancel: () => void }) => (
    <div role="dialog" aria-label="notice">
      <button onClick={onConfirmed}>notice-ok</button>
      <button onClick={onCancel}>notice-cancel</button>
    </div>
  ),
}))

const tomsk: City = { id: 2, name: 'Томск', region: 'Томская область', timeZoneId: 'Asia/Tomsk', utcOffsetMinutes: 420, label: 'Томск, Томская область' }
const moscow: City = { id: 3, name: 'Москва', region: 'Москва', timeZoneId: 'Europe/Moscow', utcOffsetMinutes: 180, label: 'Москва, Москва' }
vi.mock('@/components/ui/CityCombobox', () => ({
  CityCombobox: ({ label, value, onChange, error }: { label: string; value: City | null; onChange: (c: City | null) => void; error?: string }) => (
    <div>
      <span>{label}: {value?.name ?? 'нет'}</span>
      <button type="button" onClick={() => onChange(tomsk)}>pick-tomsk</button>
      <button type="button" onClick={() => onChange(moscow)}>pick-moscow</button>
      {error && <p role="alert">{error}</p>}
    </div>
  ),
}))

const shop = (over: Partial<ShopManageDto> = {}): ShopManageDto =>
  ({
    id: 's1', name: 'Шаурма', slug: 'shaurma', description: 'Свежая', phone: '79001234567', email: null, address: 'Ленина, 5',
    cityId: 1, cityName: 'Барнаул', cityRegion: 'Алтайский край', timeZoneId: 'Asia/Barnaul', utcOffsetMinutes: 420,
    timeZoneChangeAllowed: true, timeZoneChangeLockedText: null, yandexMapsUrl: 'https://yandex.ru/maps/org/x/1/', twoGisUrl: null,
    isActive: true, publicUrl: 'https://goods.ezbook.ru/shaurma', myRole: 'Owner', ...over,
  }) as ShopManageDto

function renderIt(s: ShopManageDto) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const invalidate = vi.spyOn(qc, 'invalidateQueries')
  const ui = (sh: ShopManageDto) => (
    <QueryClientProvider client={qc}>
      <ShopProfileSection key={sh.id} shop={sh} />
    </QueryClientProvider>
  )
  const r = render(ui(s))
  return { ...r, invalidate, rerenderShop: (sh: ShopManageDto) => r.rerender(ui(sh)) }
}

const err = (status: number, data: string) => Promise.reject({ isAxiosError: true, response: { status, data } })
const save = (user: ReturnType<typeof userEvent.setup>) => user.click(screen.getByRole('button', { name: 'Сохранить' }))

beforeEach(() => {
  update.mockReset().mockResolvedValue({})
  saveAddress.mockReset().mockResolvedValue({ company: {} })
})

describe('ShopProfileSection — ARCHITECTURE_CYCLE26.md §552', () => {
  it('has the groups and labelled fields', () => {
    renderIt(shop())
    expect(screen.getByRole('heading', { name: 'Профиль магазина' })).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Адрес и карты' })).toBeInTheDocument()
    for (const l of ['Название *', 'Описание', 'Телефон для покупателей', 'Email для покупателей', 'Адрес', 'Яндекс Карты', '2ГИС'])
      expect(screen.getByLabelText(l)).toBeInTheDocument()
    expect(screen.getByText('Виден на странице магазина')).toBeInTheDocument()
    expect(screen.getByText('Адрес виден покупателям на странице магазина')).toBeInTheDocument()
    expect(screen.getByText(/Часовой пояс: Барнаул, Алтайский край → UTC\+7, Asia\/Barnaul/)).toBeInTheDocument()
  })

  it('sends untouched links never, a cleared one as "", cityId only on change and never timeZoneId', async () => {
    const user = userEvent.setup()
    renderIt(shop())
    await save(user)
    expect(update).toHaveBeenCalledWith('s1', { name: 'Шаурма', description: 'Свежая', phone: '79001234567', email: '' })
    expect(saveAddress).not.toHaveBeenCalled()

    update.mockClear()
    await user.clear(screen.getByLabelText('Яндекс Карты'))
    await save(user)
    const body = update.mock.calls[0][1]
    expect(body.yandexMapsUrl).toBe('')
    expect(body).not.toHaveProperty('twoGisUrl')
    expect(body).not.toHaveProperty('cityId')
    expect(body).not.toHaveProperty('timeZoneId')
  })

  it('shows a 2ГИС error at the 2ГИС field when only that link was sent', async () => {
    const user = userEvent.setup()
    update.mockImplementation(() => err(400, 'Ждём ссылку на 2ГИС — например, https://2gis.ru/'))
    renderIt(shop())
    await user.type(screen.getByLabelText('2ГИС'), 'bad')
    await save(user)
    const field = await screen.findByLabelText('2ГИС')
    expect(field).toHaveAccessibleDescription('Ждём ссылку на 2ГИС — например, https://2gis.ru/')
    expect(field).toBeInvalid()
    expect(screen.getByLabelText('Яндекс Карты')).toBeValid()
  })

  it('attributes a generic «Ссылка…» error to 2ГИС when Яндекс was not in the request', async () => {
    const user = userEvent.setup()
    update.mockImplementation(() => err(400, 'Ссылка должна начинаться с https://'))
    renderIt(shop())
    await user.type(screen.getByLabelText('2ГИС'), 'bad')
    await save(user)
    expect(await screen.findByLabelText('2ГИС')).toBeInvalid()
    expect(screen.getByLabelText('Яндекс Карты')).toBeValid()
  })

  it('does not lose typed input when the shop is re-read after saving', async () => {
    const user = userEvent.setup()
    const { rerenderShop } = renderIt(shop())
    await user.type(screen.getByLabelText('Название *'), ' 2')
    await save(user)
    await user.type(screen.getByLabelText('Описание'), ' ещё')
    rerenderShop(shop({ name: 'Шаурма 2', description: 'Свежая' }))
    expect(screen.getByLabelText('Описание')).toHaveValue('Свежая ещё')
  })

  it('validates an empty name without a request', async () => {
    const user = userEvent.setup()
    renderIt(shop())
    await user.clear(screen.getByLabelText('Название *'))
    await save(user)
    expect(update).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Название *')).toBeInvalid()
  })

  it('shows the public-address notice before writing and cancel aborts the whole save', async () => {
    const user = userEvent.setup()
    renderIt(shop())
    await user.type(screen.getByLabelText('Адрес'), '7')
    await save(user)
    expect(screen.getByRole('dialog', { name: 'notice' })).toBeInTheDocument()
    expect(update).not.toHaveBeenCalled()
    await user.click(screen.getByText('notice-cancel'))
    expect(update).not.toHaveBeenCalled()
    expect(saveAddress).not.toHaveBeenCalled()

    await save(user)
    await user.click(screen.getByText('notice-ok'))
    expect(update).toHaveBeenCalledTimes(1)
    expect(saveAddress).toHaveBeenCalledWith('s1', 'Ленина, 57')
    expect(await screen.findByRole('status')).toHaveTextContent('Сохранено')
  })

  it('reports an address failure while keeping the other changes as saved', async () => {
    const user = userEvent.setup()
    saveAddress.mockImplementation(() => err(500, ''))
    renderIt(shop())
    await user.type(screen.getByLabelText('Адрес'), '7')
    await save(user)
    await user.click(screen.getByText('notice-ok'))
    expect(await screen.findByText('Остальные изменения сохранены')).toBeInTheDocument()
    expect(screen.getByLabelText('Адрес')).toHaveAccessibleDescription('Не удалось сохранить адрес. Попробуйте ещё раз.')
    expect(screen.getByLabelText('Адрес')).toHaveValue('Ленина, 57')
  })

  it('saves a city with the same UTC offset without a confirmation', async () => {
    const user = userEvent.setup()
    renderIt(shop())
    await user.click(screen.getByText('pick-tomsk'))
    await save(user)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(update.mock.calls[0][1]).toMatchObject({ cityId: 2 })
  })

  it('asks to confirm a city in another time zone; cancel sends nothing', async () => {
    const user = userEvent.setup()
    renderIt(shop())
    await user.click(screen.getByText('pick-moscow'))
    await save(user)
    const dialog = screen.getByRole('dialog', { name: 'Сменить город?' })
    expect(dialog).toHaveTextContent('Часовой пояс магазина изменится: UTC+7 → UTC+3.')
    await user.click(screen.getByRole('button', { name: 'Отмена' }))
    expect(update).not.toHaveBeenCalled()
    await save(user)
    await user.click(screen.getByRole('button', { name: 'Сменить город' }))
    expect(update.mock.calls[0][1]).toMatchObject({ cityId: 3 })
  })

  it('with timeZoneChangeAllowed=false shows the server text at the city and sends nothing', async () => {
    const user = userEvent.setup()
    renderIt(shop({ timeZoneChangeAllowed: false, timeZoneChangeLockedText: 'У магазина уже есть заказы …(UTC+7).' }))
    await user.click(screen.getByText('pick-moscow'))
    await save(user)
    expect(screen.getByRole('alert')).toHaveTextContent('У магазина уже есть заказы …(UTC+7).')
    expect(update).not.toHaveBeenCalled()
    // same offset stays allowed
    await user.click(screen.getByText('pick-tomsk'))
    await save(user)
    expect(update).toHaveBeenCalledTimes(1)
  })

  it('shows a 409 body at the city field', async () => {
    const user = userEvent.setup()
    update.mockImplementation(() => err(409, 'У магазина уже есть заказы, поэтому часовой пояс сменить нельзя'))
    renderIt(shop())
    await user.click(screen.getByText('pick-moscow'))
    await save(user)
    await user.click(screen.getByRole('button', { name: 'Сменить город' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('У магазина уже есть заказы')
  })

  it('invalidates shop, my-shops and storefront after a successful save', async () => {
    const user = userEvent.setup()
    const { invalidate } = renderIt(shop())
    await save(user)
    await screen.findByRole('status')
    const keys = invalidate.mock.calls.map((c) => JSON.stringify((c[0] as { queryKey: unknown }).queryKey))
    expect(keys).toEqual(expect.arrayContaining(['["shop","s1"]', '["my-shops"]', '["storefront"]']))
  })
})
