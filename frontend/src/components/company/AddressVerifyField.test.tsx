import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { AddressVerifyField } from './AddressVerifyField'
import type { AddressLookupResultDto, Company } from '../../types'

const getText = vi.fn()
const confirmNotice = vi.fn()
const lookup = vi.fn()
const saveAddress = vi.fn()

vi.mock('../../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
}))
vi.mock('../../api/companyAddress', () => ({
  companyAddressApi: {
    lookup: (...args: unknown[]) => lookup(...args),
    saveAddress: (...args: unknown[]) => saveAddress(...args),
    confirmNotice: (...args: unknown[]) => confirmNotice(...args),
  },
}))

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>
}

const noticeHtml = '<h2>Текст для владельца</h2><p>Этот адрес увидит любой человек в интернете.</p><h2>Подтверждение</h2><p>x</p>'

function stubCompany(): Company {
  return { id: 'c1', name: 'Гвоздь', slug: 'gvozd', allowSelfBooking: true }
}

beforeEach(() => {
  getText.mockReset()
  confirmNotice.mockReset()
  lookup.mockReset()
  saveAddress.mockReset()
  getText.mockResolvedValue({ key: 'PublicAddressNotice', version: 'v1', isDraft: true, contentHtml: noticeHtml })
})

describe('AddressVerifyField — ARCHITECTURE_CYCLE13.md §211/§220', () => {
  it('available: false renders an ordinary field — no "Проверить адрес" button', () => {
    render(<AddressVerifyField companyId="c1" initialAddress="Ленина 5" addressVerification={null} onSaved={vi.fn()} />, {
      wrapper,
    })
    expect(screen.queryByRole('button', { name: 'Проверить адрес' })).not.toBeInTheDocument()
  })

  it('available: false — editing and saving still shows the public-address notice before saving', async () => {
    const user = userEvent.setup()
    render(<AddressVerifyField companyId="c1" initialAddress="Ленина 5" addressVerification={null} onSaved={vi.fn()} />, {
      wrapper,
    })

    await user.type(screen.getByLabelText('Адрес'), ', 7')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    expect(await screen.findByText('Адрес станет общедоступным')).toBeInTheDocument()
    expect(saveAddress).not.toHaveBeenCalled()
  })

  it('available: true — shows the "Проверить адрес" button and status line', () => {
    render(
      <AddressVerifyField
        companyId="c1"
        initialAddress="Ленина 5"
        addressVerification={{ available: true, status: 'Unverified', verifiedAt: null, precision: null }}
        onSaved={vi.fn()}
      />,
      { wrapper },
    )
    expect(screen.getByRole('button', { name: 'Проверить адрес' })).toBeInTheDocument()
    expect(screen.getByText('Не подтверждён')).toBeInTheDocument()
  })

  it('picking a candidate fills the field but does not save anything', async () => {
    const result: AddressLookupResultDto = {
      outcome: 'Ok',
      queriedAddress: 'Барнаул, Ленина 5',
      candidates: [
        { formattedAddress: 'Россия, Алтайский край, Барнаул, проспект Ленина, 5', precision: 'House', cityName: 'Барнаул', point: null, warnings: [] },
      ],
      warnings: [],
      attribution: '© Яндекс',
    }
    lookup.mockResolvedValue(result)
    const user = userEvent.setup()
    render(
      <AddressVerifyField
        companyId="c1"
        initialAddress="Ленина 5"
        addressVerification={{ available: true, status: 'Unverified', verifiedAt: null, precision: null }}
        onSaved={vi.fn()}
      />,
      { wrapper },
    )

    await user.click(screen.getByRole('button', { name: 'Проверить адрес' }))
    const candidate = await screen.findByRole('button', { name: /проспект Ленина, 5/ })
    await user.click(candidate)

    expect(screen.getByLabelText('Адрес')).toHaveValue('Россия, Алтайский край, Барнаул, проспект Ленина, 5')
    expect(saveAddress).not.toHaveBeenCalled()
    expect(screen.getByText('© Яндекс')).toBeInTheDocument()
  })

  it('links warnings to the field via aria-describedby', async () => {
    const result: AddressLookupResultDto = {
      outcome: 'Ok',
      queriedAddress: 'Барнаул, Ленина',
      candidates: [{ formattedAddress: 'Ленина', precision: 'Street', cityName: 'Барнаул', point: null, warnings: [] }],
      warnings: [{ code: 'PrecisionStreet', message: 'Найдена только улица — подтверждение не поставлено.' }],
      attribution: '© Яндекс',
    }
    lookup.mockResolvedValue(result)
    const user = userEvent.setup()
    render(
      <AddressVerifyField
        companyId="c1"
        initialAddress="Ленина"
        addressVerification={{ available: true, status: 'Unverified', verifiedAt: null, precision: null }}
        onSaved={vi.fn()}
      />,
      { wrapper },
    )

    await user.click(screen.getByRole('button', { name: 'Проверить адрес' }))
    await screen.findByText('Найдена только улица — подтверждение не поставлено.')

    const input = screen.getByLabelText('Адрес')
    const describedBy = input.getAttribute('aria-describedby')
    expect(describedBy).toBeTruthy()
    expect(document.getElementById(describedBy!)).toHaveTextContent('Найдена только улица')
  })

  it('Unavailable shows a human message and leaves saving unblocked', async () => {
    const result: AddressLookupResultDto = {
      outcome: 'Unavailable',
      queriedAddress: 'Барнаул, Ленина 5',
      candidates: [],
      warnings: [{ code: 'Unavailable', message: 'Не удалось проверить адрес, попробуйте позже.' }],
      attribution: '',
    }
    lookup.mockResolvedValue(result)
    const user = userEvent.setup()
    render(
      <AddressVerifyField
        companyId="c1"
        initialAddress="Ленина 5"
        addressVerification={{ available: true, status: 'Unverified', verifiedAt: null, precision: null }}
        onSaved={vi.fn()}
      />,
      { wrapper },
    )

    await user.click(screen.getByRole('button', { name: 'Проверить адрес' }))
    expect(await screen.findByText(/Не удалось проверить адрес, попробуйте позже/)).toBeInTheDocument()

    await user.type(screen.getByLabelText('Адрес'), ', 7')
    expect(screen.getByRole('button', { name: 'Сохранить' })).not.toBeDisabled()
  })

  it('renders per-candidate warnings (e.g. CityMismatch), not just top-level ones', async () => {
    const result: AddressLookupResultDto = {
      outcome: 'Ok',
      queriedAddress: 'Барнаул, Ленина 5',
      candidates: [
        {
          formattedAddress: 'Россия, Новосибирская область, Ленина, 5',
          precision: 'House',
          cityName: 'Новосибирск',
          point: null,
          warnings: [{ code: 'CityMismatch', message: 'Адрес найден в другом городе — проверьте часовой пояс.' }],
        },
      ],
      warnings: [],
      attribution: '© Яндекс',
    }
    lookup.mockResolvedValue(result)
    const user = userEvent.setup()
    render(
      <AddressVerifyField
        companyId="c1"
        initialAddress="Ленина 5"
        addressVerification={{ available: true, status: 'Unverified', verifiedAt: null, precision: null }}
        onSaved={vi.fn()}
      />,
      { wrapper },
    )

    await user.click(screen.getByRole('button', { name: 'Проверить адрес' }))
    expect(await screen.findByText('Адрес найден в другом городе — проверьте часовой пояс.')).toBeInTheDocument()
  })

  it('after a save with outcome Unavailable: shows the server-composed warning and offers a retry button even though the text is unchanged', async () => {
    confirmNotice.mockResolvedValue({ version: 'v1', acknowledgedAt: '2026-09-24T10:00:00Z' })
    saveAddress.mockResolvedValue({
      company: stubCompany(),
      verification: {
        outcome: 'Unavailable',
        status: 'Unverified',
        verifiedAt: null,
        precision: null,
        // §236/§237 — the server always populates `warnings` for `Empty`/`Unavailable` on save
        // (`AddressWarnings.NotFound`/`Unavailable`); the component must render THIS text, not invent
        // its own second wording for the same outcome.
        warnings: [{ code: 'Unavailable', message: 'Не удалось проверить адрес, попробуйте позже.' }],
        attribution: '',
      },
    })
    const user = userEvent.setup()
    render(
      <AddressVerifyField
        companyId="c1"
        initialAddress="Ленина 5"
        addressVerification={{ available: true, status: 'Unverified', verifiedAt: null, precision: null }}
        onSaved={vi.fn()}
      />,
      { wrapper },
    )

    await user.type(screen.getByLabelText('Адрес'), ', 7')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await user.click(await screen.findByRole('button', { name: 'Понятно, сохранить адрес' }))

    await waitFor(() => expect(saveAddress).toHaveBeenCalled())
    expect(await screen.findByText('Не удалось проверить адрес, попробуйте позже.')).toBeInTheDocument()
    // The field's live value now equals `initialAddress` again (save reset `dirty`), yet a retry
    // affordance must still be present — this is the "no way to retry" gap the review flagged.
    expect(screen.getByRole('button', { name: 'Повторить проверку' })).toBeInTheDocument()
  })

  it('after a save with outcome Ok: no outcome message, no lingering retry button', async () => {
    confirmNotice.mockResolvedValue({ version: 'v1', acknowledgedAt: '2026-09-24T10:00:00Z' })
    saveAddress.mockResolvedValue({
      company: stubCompany(),
      verification: { outcome: 'Ok', status: 'Verified', verifiedAt: '2026-09-24T10:00:00Z', precision: 'House', warnings: [], attribution: '© Яндекс' },
    })
    const user = userEvent.setup()
    render(
      <AddressVerifyField
        companyId="c1"
        initialAddress="Ленина 5"
        addressVerification={{ available: true, status: 'Unverified', verifiedAt: null, precision: null }}
        onSaved={vi.fn()}
      />,
      { wrapper },
    )

    await user.type(screen.getByLabelText('Адрес'), ', 7')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await user.click(await screen.findByRole('button', { name: 'Понятно, сохранить адрес' }))

    await waitFor(() => expect(saveAddress).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: 'Повторить проверку' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Сохранить' })).not.toBeInTheDocument()
  })

  it('end to end: edit, save, confirm the notice — saveAddress is called with the new text and verify=available', async () => {
    confirmNotice.mockResolvedValue({ version: 'v1', acknowledgedAt: '2026-09-24T10:00:00Z' })
    saveAddress.mockResolvedValue({
      company: stubCompany(),
      verification: { outcome: 'Ok', status: 'Verified', verifiedAt: '2026-09-24T10:00:00Z', precision: 'House', warnings: [], attribution: '© Яндекс' },
    })
    const onSaved = vi.fn()
    const user = userEvent.setup()
    render(
      <AddressVerifyField
        companyId="c1"
        initialAddress="Ленина 5"
        addressVerification={{ available: true, status: 'Unverified', verifiedAt: null, precision: null }}
        onSaved={onSaved}
      />,
      { wrapper },
    )

    await user.type(screen.getByLabelText('Адрес'), ', 7')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await user.click(await screen.findByRole('button', { name: 'Понятно, сохранить адрес' }))

    await waitFor(() => expect(saveAddress).toHaveBeenCalledWith('c1', 'Ленина 5, 7', true))
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(stubCompany()))
  })
})
