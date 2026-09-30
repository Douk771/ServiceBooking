import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor, act } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CompanyProfileCard, type CompanyProfileSnapshot, type CompanyProfileTexts } from './CompanyProfileCard'

const update = vi.fn()
vi.mock('../../api/companies', () => ({ companiesApi: { update: (...a: unknown[]) => update(...a), uploadLogo: vi.fn() } }))
vi.mock('../../api/companyAddress', () => ({ companyAddressApi: { saveAddress: vi.fn() } }))
vi.mock('../../api/cities', () => ({ citiesApi: { search: () => Promise.resolve([]) } }))

const texts: CompanyProfileTexts = {
  title: 'Профиль', logoAlt: 'Лого', nameRequired: 'Введите название', phoneLabel: 'Телефон', emailLabel: 'Email',
  emailHint: 'eh', cityHint: 'Подсказка города', addressHint: 'ah', saveFailed: 'Не удалось сохранить.',
  timeZoneChange: (f, t) => `${f} → ${t}`,
}
const snap: CompanyProfileSnapshot = {
  id: 'c1', name: 'Имя', description: null, phone: null, email: null, logoUrl: null, address: null, yandexMapsUrl: null, twoGisUrl: null,
  city: { id: 1, name: 'Барнаул', region: '', timeZoneId: 'Asia/Barnaul', utcOffsetMinutes: 420, label: 'Барнаул' },
  zone: { id: 'Asia/Barnaul', offsetMinutes: 420, isManual: false },
}
const fail = (status: number, data: string) => ({ isAxiosError: true, response: { status, data } })
let failWith: unknown = null

function renderCard(props: Partial<React.ComponentProps<typeof CompanyProfileCard>> = {}) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onChanged = vi.fn()
  render(
    <QueryClientProvider client={qc}>
      <CompanyProfileCard company={snap} texts={texts} errorMessage={(_e, fb) => `mapped:${fb}`} onChanged={onChanged} {...props} />
    </QueryClientProvider>,
  )
  return { onChanged }
}
const save = () => userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))

beforeEach(() => {
  failWith = null
  update.mockReset().mockImplementation(() => (failWith ? Promise.reject(failWith) : Promise.resolve({})))
})

describe('CompanyProfileCard — error routing (ARCHITECTURE_CYCLE32.md §32.4.6)', () => {
  it('409 without timeZoneLock goes under the form', async () => {
    failWith = fail(409, 'конфликт')
    renderCard()
    await save()
    expect(await screen.findByRole('alert')).toHaveTextContent('mapped:Не удалось сохранить.')
  })

  it('409 with timeZoneLock goes to the city field', async () => {
    failWith = fail(409, 'конфликт')
    renderCard({ timeZoneLock: { allowed: true, lockedText: null, fallbackText: 'fb' } })
    await save()
    const alert = await screen.findByText('mapped:Не удалось сохранить.')
    expect(screen.getByRole('combobox')).toHaveAccessibleDescription(expect.stringContaining('mapped:'))
    expect(alert.tagName).toBe('P')
  })

  it('"Неизвестный часовой пояс" without manualTimeZone goes under the form', async () => {
    failWith = fail(400, 'Неизвестный часовой пояс')
    renderCard()
    await save()
    expect(await screen.findByRole('alert')).toHaveTextContent('mapped:Не удалось сохранить.')
  })

  it('"Город не найден" goes to the city field as the raw text', async () => {
    failWith = fail(400, 'Город не найден')
    renderCard()
    await save()
    await waitFor(() => expect(screen.getByRole('combobox')).toHaveAccessibleDescription(expect.stringContaining('Город не найден')))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('a generic "Ссылка…" goes to 2ГИС when Yandex was not sent, to Yandex when it was', async () => {
    failWith = fail(400, 'Ссылка должна начинаться с https://')
    renderCard()
    await userEvent.type(screen.getByLabelText('2ГИС'), 'x')
    await save()
    await waitFor(() => expect(screen.getByLabelText('2ГИС')).toHaveAccessibleDescription('Ссылка должна начинаться с https://'))
    expect(screen.getByLabelText('Яндекс Карты')).not.toHaveAccessibleDescription('Ссылка должна начинаться с https://')
  })

  it('the city hint is linked to the combobox', () => {
    renderCard()
    expect(screen.getByRole('combobox')).toHaveAccessibleDescription(expect.stringContaining('Подсказка города'))
  })
})

describe('CompanyProfileCard — saved note', () => {
  beforeEach(() => vi.useFakeTimers({ shouldAdvanceTime: true }))
  afterEach(() => vi.useRealTimers())

  it('"Сохранено" is role=status and disappears after 2.5 s', async () => {
    const { onChanged } = renderCard()
    await save()
    expect(await screen.findByRole('status')).toHaveTextContent('Сохранено')
    expect(onChanged).toHaveBeenCalledTimes(1)
    act(() => {
      vi.advanceTimersByTime(2600)
    })
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})
