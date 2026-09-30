/**
 * QA цикла 32 (`QA32-`): сквозные сценарии вкладки «Настройки» салона по критериям приёмки SPEC.md (US-32-02..04).
 * Написано по SPEC.md, без опоры на реализацию: настоящие CityCombobox/PhoneInput/Modal, мокается только сеть.
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SettingsTab } from './CompanyManagePage'
import { CANCEL_WINDOW_FIELD_CAPTION } from '../../legal/staffNotices'

const update = vi.fn()
const getMy = vi.fn()
const saveAddress = vi.fn()
const search = vi.fn()

vi.mock('../../api/companies', () => ({
  companiesApi: {
    getMy: (...a: unknown[]) => getMy(...a),
    update: (...a: unknown[]) => update(...a),
    uploadLogo: vi.fn(),
    getPhotoUsage: () => Promise.resolve({ companyId: 'co1', usedBytes: 0, photoCount: 0, quotaMb: 100, percentUsed: 0, retention: 'SixMonths' }),
  },
}))
vi.mock('../../api/companyAddress', () => ({
  companyAddressApi: { saveAddress: (...a: unknown[]) => saveAddress(...a), notice: vi.fn() },
}))
vi.mock('../../api/companyCatalogListing', () => ({
  companyCatalogListingApi: {
    get: () =>
      Promise.resolve({ showInCatalog: false, allowedByPlan: true, visible: false, statusText: 'x', notAllowedByPlanText: null, checklist: [] }),
    put: vi.fn(),
  },
}))
vi.mock('../../api/companyPhotos', () => ({ companyPhotosApi: { list: () => Promise.resolve([]) } }))
vi.mock('../../api/cities', () => ({ citiesApi: { search: (...a: unknown[]) => search(...a) } }))
vi.mock('../../api/services', () => ({ servicesApi: { getByCompany: () => Promise.resolve([]) } }))
vi.mock('../../components/company/PublicAddressNotice', () => ({
  PublicAddressNotice: ({ onConfirmed, onCancel }: { onConfirmed: () => void; onCancel: () => void }) => (
    <div role="dialog" aria-label="Адрес виден клиентам">
      <button onClick={onConfirmed}>Понятно, сохранить</button>
      <button onClick={onCancel}>Не сохранять</button>
    </div>
  ),
}))

const TOMSK = { id: 6, name: 'Томск', region: 'Томская область', timeZoneId: 'Asia/Tomsk', utcOffsetMinutes: 420, label: 'Томск, Томская область' }
const MOSCOW = { id: 7, name: 'Москва', region: 'Москва', timeZoneId: 'Europe/Moscow', utcOffsetMinutes: 180, label: 'Москва, Москва' }

const COMPANY = {
  id: 'co1', name: 'Салон', slug: 'salon', description: 'Стрижки', phone: '79001234567', email: 'a@b.ru',
  address: 'Ленина, 1', cityId: 5, cityName: 'Барнаул', cityRegion: 'Алтайский край', timeZoneId: 'Asia/Barnaul',
  utcOffsetMinutes: 420, timeZoneIsManual: false, allowSelfBooking: true, requirePrepayment: false,
  bookingHorizonDays: 30, clientRescheduleMinHours: 4,
  planAllowsOnlineBooking: true, planAllowsOnlinePayment: true, planAllowsPublicListing: true,
}

let qc: QueryClient
function renderTab() {
  qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <SettingsTab companyId="co1" />
    </QueryClientProvider>,
  )
}
const rejectWith = (status: number, data: string) => Promise.reject({ isAxiosError: true, response: { status, data } })
const saveProfile = () => userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
const saveRules = () => userEvent.click(screen.getByRole('button', { name: 'Сохранить правила' }))

async function pickCity(city: typeof TOMSK) {
  search.mockResolvedValue([city])
  const box = screen.getByRole('combobox', { name: 'Город' })
  await userEvent.clear(box)
  await userEvent.type(box, city.name)
  await userEvent.click(await screen.findByRole('option', { name: new RegExp(city.name) }))
}

beforeEach(() => {
  update.mockReset().mockResolvedValue({})
  saveAddress.mockReset().mockResolvedValue({})
  search.mockReset().mockResolvedValue([])
  getMy.mockReset().mockResolvedValue([COMPANY])
})
afterEach(() => vi.useRealTimers())

describe('QA32 — US-32-02 сохранение профиля одной кнопкой', () => {
  it('QA32-01: правка названия и телефона — один PUT, без города, пояса, адреса, ссылок и rules-полей', async () => {
    renderTab()
    const name = await screen.findByLabelText('Название *')
    await userEvent.clear(name)
    await userEvent.type(name, 'Новый салон')
    const phone = screen.getByLabelText('Телефон для клиентов')
    await userEvent.clear(phone)
    await userEvent.type(phone, '9005554433')
    await saveProfile()
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    const [id, body] = update.mock.calls[0] as [string, Record<string, unknown>]
    expect(id).toBe('co1')
    expect(body).toMatchObject({ name: 'Новый салон', phone: '79005554433' })
    for (const k of ['cityId', 'timeZoneId', 'address', 'yandexMapsUrl', 'twoGisUrl', 'bookingHorizonDays', 'clientRescheduleMinHours', 'allowSelfBooking', 'requirePrepayment', 'showInPublicListing'])
      expect(body).not.toHaveProperty(k)
    expect(saveAddress).not.toHaveBeenCalled()
    expect(await screen.findByText('Сохранено')).toHaveAttribute('role', 'status')
  })

  it('QA32-02: кнопка «Сохранить» активна сразу, без правок (Q-32-11), и не пишет лишнего', async () => {
    renderTab()
    await screen.findByLabelText('Название *')
    const btn = screen.getByRole('button', { name: 'Сохранить' })
    expect(btn).toBeEnabled()
    await userEvent.click(btn)
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(saveAddress).not.toHaveBeenCalled()
  })

  it('QA32-03: пустое и пробельное название — ошибка у поля и ни одного запроса', async () => {
    renderTab()
    const name = await screen.findByLabelText('Название *')
    await userEvent.clear(name)
    await userEvent.type(name, '   ')
    await saveProfile()
    expect(await screen.findByText('Введите название салона')).toBeInTheDocument()
    expect(update).not.toHaveBeenCalled()
    expect(saveAddress).not.toHaveBeenCalled()
  })

  it('QA32-04: город с другим смещением — окно «Сменить город?»; «Отмена» ничего не шлёт', async () => {
    renderTab()
    await screen.findByLabelText('Название *')
    await pickCity(MOSCOW)
    await saveProfile()
    const dlg = await screen.findByRole('dialog')
    expect(within(dlg).getByText(/UTC\+7.*→.*UTC\+3|\+7.*\+3/)).toBeInTheDocument()
    expect(dlg).toHaveTextContent('салона')
    await userEvent.click(within(dlg).getByRole('button', { name: 'Отмена' }))
    expect(update).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('QA32-05: «Сменить город» продолжает: в PUT уходит cityId', async () => {
    renderTab()
    await screen.findByLabelText('Название *')
    await pickCity(MOSCOW)
    await saveProfile()
    await userEvent.click(await screen.findByRole('button', { name: 'Сменить город' }))
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update.mock.calls[0][1]).toMatchObject({ cityId: 7 })
    expect(update.mock.calls[0][1]).not.toHaveProperty('address')
  })

  it('QA32-06: город с тем же смещением — без окна, cityId в общем PUT', async () => {
    renderTab()
    await screen.findByLabelText('Название *')
    await pickCity(TOMSK)
    await saveProfile()
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(screen.queryByRole('button', { name: 'Сменить город' })).not.toBeInTheDocument()
    expect(update.mock.calls[0][1]).toMatchObject({ cityId: 6 })
  })

  it('QA32-07: смена адреса — сначала уведомление, основной PUT раньше PUT адреса; «Не сохранять» ничего не шлёт', async () => {
    renderTab()
    const addr = await screen.findByLabelText('Адрес')
    await userEvent.clear(addr)
    await userEvent.type(addr, 'Мира, 5')
    await saveProfile()
    await userEvent.click(await screen.findByRole('button', { name: 'Не сохранять' }))
    expect(update).not.toHaveBeenCalled()
    expect(saveAddress).not.toHaveBeenCalled()

    await saveProfile()
    await userEvent.click(await screen.findByRole('button', { name: 'Понятно, сохранить' }))
    await waitFor(() => expect(saveAddress).toHaveBeenCalledWith('co1', 'Мира, 5'))
    expect(update.mock.invocationCallOrder[0]).toBeLessThan(saveAddress.mock.invocationCallOrder[0])
    expect(update.mock.calls[0][1]).not.toHaveProperty('address')
  })

  it('QA32-08: сбой адреса — ошибка у поля и «Остальные изменения сохранены», профиль уже ушёл', async () => {
    saveAddress.mockReset().mockImplementation(() => rejectWith(500, 'boom'))
    renderTab()
    const addr = await screen.findByLabelText('Адрес')
    await userEvent.clear(addr)
    await userEvent.type(addr, 'Мира, 5')
    await saveProfile()
    await userEvent.click(await screen.findByRole('button', { name: 'Понятно, сохранить' }))
    expect(await screen.findByText('Остальные изменения сохранены')).toBeInTheDocument()
    expect(update).toHaveBeenCalledTimes(1)
    expect(screen.getByLabelText('Адрес')).toHaveValue('Мира, 5')
    expect(screen.queryByText('Сохранено')).not.toBeInTheDocument()
  })

  it('QA32-09: ручной пояс: timeZoneId уходит; снятие галки после сохранения шлёт timeZoneId: null', async () => {
    renderTab()
    await userEvent.click(await screen.findByLabelText(/Указать часовой пояс вручную/))
    await userEvent.type(screen.getByLabelText('Часовой пояс (IANA)'), 'Asia/Tomsk')
    await saveProfile()
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update.mock.calls[0][1]).toMatchObject({ timeZoneId: 'Asia/Tomsk' })

    await userEvent.click(screen.getByLabelText(/Указать часовой пояс вручную/))
    await saveProfile()
    // Сброс к поясу города: клиент не знает смещение пояса города, поэтому всегда спрашивает (ARCHITECTURE_CYCLE32 §32.6, вариант В).
    await userEvent.click(await screen.findByRole('button', { name: 'Сменить город' }))
    await waitFor(() => expect(update).toHaveBeenCalledTimes(2))
    expect(update.mock.calls[1][1]).toHaveProperty('timeZoneId', null)
  })

  it('QA32-10: ручной пояс с другим смещением — то же окно подтверждения', async () => {
    renderTab()
    await userEvent.click(await screen.findByLabelText(/Указать часовой пояс вручную/))
    await userEvent.type(screen.getByLabelText('Часовой пояс (IANA)'), 'Europe/Moscow')
    await saveProfile()
    expect(await screen.findByRole('button', { name: 'Сменить город' })).toBeInTheDocument()
    expect(update).not.toHaveBeenCalled()
  })

  it('QA32-11: 400 по Яндекс-ссылке — ошибка у поля «Яндекс Карты»', async () => {
    update.mockReset().mockImplementation(() => rejectWith(400, 'Ссылка на Яндекс Карты должна вести на yandex.ru/maps'))
    renderTab()
    const y = await screen.findByLabelText('Яндекс Карты')
    await userEvent.type(y, 'http://evil.example')
    await saveProfile()
    const msg = await screen.findByText(/Яндекс Карты должна вести/)
    expect(y.getAttribute('aria-describedby') ?? '').toContain(msg.id)
    expect(screen.queryByText('Сохранено')).not.toBeInTheDocument()
  })

  it('QA32-12: 400 «Город не найден» — у поля города', async () => {
    update.mockReset().mockImplementation(() => rejectWith(400, 'Город не найден'))
    renderTab()
    await screen.findByLabelText('Название *')
    await pickCity(TOMSK)
    await saveProfile()
    const msg = await screen.findByText('Город не найден')
    expect(msg.closest('div')).toContainElement(screen.getByRole('combobox', { name: 'Город' }))
  })

  it('QA32-13: сетевой обрыв (нет ответа) — сообщение под формой, введённый текст остаётся', async () => {
    update.mockReset().mockImplementation(() => Promise.reject(new Error('Network Error')))
    renderTab()
    const name = await screen.findByLabelText('Название *')
    await userEvent.type(name, ' 2')
    await saveProfile()
    await waitFor(() => expect(update).toHaveBeenCalled())
    await waitFor(() => expect(screen.queryByText('Сохранено')).not.toBeInTheDocument())
    expect(screen.getByLabelText('Название *')).toHaveValue('Салон 2')
    expect(screen.getByRole('button', { name: 'Сохранить' })).toBeEnabled()
  })

  it('QA32-14: двойной клик/Enter во время сохранения — один PUT', async () => {
    let release: (v?: unknown) => void = () => {}
    update.mockReset().mockImplementation(() => new Promise((r) => { release = r }))
    renderTab()
    await screen.findByLabelText('Название *')
    const btn = screen.getByRole('button', { name: 'Сохранить' })
    await userEvent.click(btn)
    await userEvent.type(screen.getByLabelText('Название *'), '{Enter}')
    await userEvent.click(btn)
    expect(update).toHaveBeenCalledTimes(1)
    release({})
    await screen.findByText('Сохранено')
  })

  it('QA32-15: «Сохранено» исчезает примерно через 2,5 с', async () => {
    renderTab()
    await screen.findByLabelText('Название *')
    vi.useFakeTimers({ shouldAdvanceTime: true })
    await userEvent.setup({ advanceTimers: vi.advanceTimersByTime }).click(screen.getByRole('button', { name: 'Сохранить' }))
    await screen.findByText('Сохранено')
    vi.advanceTimersByTime(2400)
    expect(screen.queryByText('Сохранено')).toBeInTheDocument()
    vi.advanceTimersByTime(300)
    await waitFor(() => expect(screen.queryByText('Сохранено')).not.toBeInTheDocument())
  })

  it('QA32-16: набранный текст не теряется, когда my-companies перечитывается с сервера', async () => {
    renderTab()
    const name = await screen.findByLabelText('Название *')
    await userEvent.type(name, ' Черновик')
    getMy.mockResolvedValue([{ ...COMPANY, name: 'Серверное имя', description: 'other' }])
    await qc.invalidateQueries({ queryKey: ['my-companies'] })
    await waitFor(() => expect(getMy.mock.calls.length).toBeGreaterThan(1))
    expect(screen.getByLabelText('Название *')).toHaveValue('Салон Черновик')
  })

  it('QA32-17: после успеха данные перечитываются (my-companies инвалидируется)', async () => {
    renderTab()
    await screen.findByLabelText('Название *')
    const before = getMy.mock.calls.length
    await saveProfile()
    await waitFor(() => expect(getMy.mock.calls.length).toBeGreaterThan(before))
  })
})

describe('QA32 — US-32-03 «Правила записи»', () => {
  it('QA32-20: подпись окна переноса дословно, поля предзаполнены', async () => {
    renderTab()
    expect(await screen.findByText(CANCEL_WINDOW_FIELD_CAPTION)).toBeInTheDocument()
    expect(screen.getByLabelText(/На сколько дней вперёд/)).toHaveValue(30)
    expect(screen.getByLabelText(/За сколько часов/)).toHaveValue(4)
  })

  it('QA32-21: «Сохранить правила» шлёт только поля правил', async () => {
    renderTab()
    const h = await screen.findByLabelText(/На сколько дней вперёд/)
    await userEvent.clear(h)
    await userEvent.type(h, '60')
    await saveRules()
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    const body = update.mock.calls[0][1] as Record<string, unknown>
    expect(body).toMatchObject({ bookingHorizonDays: 60, clientRescheduleMinHours: 4, allowSelfBooking: true })
    for (const k of ['name', 'phone', 'email', 'description', 'cityId', 'timeZoneId', 'address', 'yandexMapsUrl', 'twoGisUrl'])
      expect(body).not.toHaveProperty(k)
  })

  it('QA32-22: пустое окно переноса не отправляется; пустой горизонт = 0', async () => {
    renderTab()
    const w = await screen.findByLabelText(/За сколько часов/)
    await userEvent.clear(w)
    await userEvent.clear(screen.getByLabelText(/На сколько дней вперёд/))
    await saveRules()
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    const body = update.mock.calls[0][1] as Record<string, unknown>
    expect(body).not.toHaveProperty('clientRescheduleMinHours')
    expect(body).toHaveProperty('bookingHorizonDays', 0)
  })

  it('QA32-23: горизонт 366 и -1 отклоняются без запроса', async () => {
    renderTab()
    const h = await screen.findByLabelText(/На сколько дней вперёд/)
    for (const v of ['366', '-1']) {
      await userEvent.clear(h)
      await userEvent.type(h, v)
      await saveRules()
      expect(await screen.findByText(/от 1 до 365/)).toBeInTheDocument()
    }
    expect(update).not.toHaveBeenCalled()
  })

  it('QA32-24: 400 окна переноса — ошибка у поля окна', async () => {
    update.mockReset().mockImplementation(() => rejectWith(400, 'Окно переноса и отмены — от 0 до 168 часов'))
    renderTab()
    const w = await screen.findByLabelText(/За сколько часов/)
    await userEvent.clear(w)
    await userEvent.type(w, '500')
    await saveRules()
    const msg = await screen.findByText(/от 0 до 168/)
    expect(w.getAttribute('aria-describedby') ?? '').toContain(msg.id)
  })

  it('QA32-25: тариф без онлайн-записи и оплаты — флаги заблокированы, не отправляются, тексты прежние', async () => {
    getMy.mockResolvedValue([{ ...COMPANY, planAllowsOnlineBooking: false, planAllowsOnlinePayment: false, allowSelfBooking: false }])
    renderTab()
    const self = await screen.findByLabelText('Разрешить клиентам записываться самостоятельно')
    expect(self).toBeDisabled()
    expect(screen.getByLabelText('Требовать предоплату при онлайн-записи')).toBeDisabled()
    expect(screen.getByText('Онлайн-запись не входит в текущий тариф — повысьте тариф, чтобы включить')).toBeInTheDocument()
    expect(screen.getByText('Онлайн-оплата не входит в текущий тариф — повысьте тариф, чтобы включить')).toBeInTheDocument()
    await saveRules()
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update.mock.calls[0][1]).not.toHaveProperty('allowSelfBooking')
    expect(update.mock.calls[0][1]).not.toHaveProperty('requirePrepayment')
  })

  it('QA32-26: несохранённые правки профиля и правил независимы друг от друга', async () => {
    renderTab()
    const name = await screen.findByLabelText('Название *')
    await userEvent.type(name, ' X')
    const h = screen.getByLabelText(/На сколько дней вперёд/)
    await userEvent.clear(h)
    await userEvent.type(h, '45')

    await saveRules()
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(getMy.mock.calls.length).toBeGreaterThan(1))
    expect(screen.getByLabelText('Название *')).toHaveValue('Салон X')

    await userEvent.type(h, '0')
    await saveProfile()
    await waitFor(() => expect(update).toHaveBeenCalledTimes(2))
    const profileBody = update.mock.calls[1][1] as Record<string, unknown>
    expect(profileBody).toMatchObject({ name: 'Салон X' })
    expect(profileBody).not.toHaveProperty('bookingHorizonDays')
    expect(screen.getByLabelText(/На сколько дней вперёд/)).toHaveValue(450)
  })
})

describe('QA32 — US-32-04 раскладка и доступность', () => {
  it('QA32-30: порядок карточек, один h1-уровень не нарушен, нет «Настройки компании», нет h3', async () => {
    renderTab()
    await screen.findByRole('heading', { name: 'Хранилище фото клиентов' })
    expect(screen.getAllByRole('heading').map((h) => `${h.tagName}:${h.textContent}`)).toEqual([
      'H2:Профиль салона', 'H2:Фотографии салона', 'H2:Правила записи', 'H2:Каталог ezbook.ru', 'H2:Виджет для сайта', 'H2:Хранилище фото клиентов',
    ])
    expect(screen.queryByText('Настройки компании')).not.toBeInTheDocument()
  })

  it('QA32-31: у всех заголовков карточек один и тот же класс стиля', async () => {
    renderTab()
    await screen.findByRole('heading', { name: 'Хранилище фото клиентов' })
    const classes = screen.getAllByRole('heading', { level: 2 }).map((h) => h.className)
    for (const c of classes) expect(c).toContain('text-[15px]')
    for (const c of classes) expect(c).toContain('font-semibold')
  })

  it('QA32-32: колонка до 760 px, одинаковый gap-5, никаких mt-[18px] у прямых детей', async () => {
    const { container } = renderTab()
    await screen.findByRole('heading', { name: 'Хранилище фото клиентов' })
    const col = container.firstElementChild as HTMLElement
    expect(col).toHaveClass('max-w-[760px]', 'flex', 'flex-col', 'gap-5')
    for (const child of Array.from(col.children)) expect(child.className).not.toMatch(/mt-\[18px\]/)
  })

  it('QA32-33: подсказки связаны через aria-describedby, «Сохранено» — role=status, alt логотипа', async () => {
    getMy.mockResolvedValue([{ ...COMPANY, logoUrl: '/l.png' }])
    renderTab()
    const email = await screen.findByLabelText('Email для клиентов')
    const hint = screen.getByText('Виден на странице салона')
    expect(email.getAttribute('aria-describedby')).toContain(hint.id)
    const city = screen.getByRole('combobox', { name: 'Город' })
    expect(city.getAttribute('aria-describedby')).toBeTruthy()
    expect(screen.getByAltText('Логотип салона')).toBeInTheDocument()
    await saveProfile()
    expect(await screen.findByText('Сохранено')).toHaveAttribute('role', 'status')
  })

  it('QA32-34: старые элементы убраны — нет второй «Сохранить»-кнопки адреса и подблока города', async () => {
    renderTab()
    await screen.findByLabelText('Название *')
    expect(screen.getAllByRole('button', { name: /^Сохранить/ }).map((b) => b.textContent)).toEqual(['Сохранить', 'Сохранить правила'])
    expect(screen.queryByText('Город и часовой пояс')).not.toBeInTheDocument()
    expect(screen.queryByText('Сохранить адрес')).not.toBeInTheDocument()
  })
})
