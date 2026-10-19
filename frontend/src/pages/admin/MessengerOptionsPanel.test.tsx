import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MessengerOptionsPanel } from './MessengerOptionsPanel'
import type { AdminMessengerOption, AdminMessengerOptions, MessengerPricePreview } from '../../types'

const messengerOptions = vi.fn()
const previewMessengerPrice = vi.fn()
const updateMessengerPrice = vi.fn()
vi.mock('../../api/adminBilling', () => ({
  adminBillingApi: {
    messengerOptions: (...a: unknown[]) => messengerOptions(...a),
    previewMessengerPrice: (...a: unknown[]) => previewMessengerPrice(...a),
    updateMessengerPrice: (...a: unknown[]) => updateMessengerPrice(...a),
  },
}))

const max = (o: Partial<AdminMessengerOption> = {}): AdminMessengerOption => ({
  transport: 'Max', label: 'MAX', optionId: 'o-max', open: true, isActive: true, pricePerMonth: null, priceText: 'не задана',
  sale: { sellable: false, reason: 'NoPrice', text: 'Не продаётся: цена не задана' }, activePaidAccounts: 0, history: [], ...o,
})
const whatsApp = (): AdminMessengerOption => ({
  transport: 'WhatsApp', label: 'WhatsApp', optionId: 'o-wa', open: false, isActive: true, pricePerMonth: 490, priceText: '490 ₽/мес',
  sale: { sellable: false, reason: 'Closed', text: 'Не продаётся: закрыт для владельцев' }, activePaidAccounts: 3,
  history: [{
    changedAt: '2026-10-18T10:00:00Z', changedByName: 'Иван', oldPricePerMonth: 390, newPricePerMonth: 490, oldPriceText: '390 ₽/мес', newPriceText: '490 ₽/мес',
    oldIsActive: true, newIsActive: true, comment: 'индексация', source: 'MessengerPriceScreen', activePaidAccounts: 3,
  }],
})
const catalog = (items: AdminMessengerOption[] = [whatsApp(), max()]): AdminMessengerOptions => ({
  items, planMatrixNote: 'note', priceHint: 'Цена действует для всех аккаунтов и всех линеек.',
})
const preview = (o: Partial<MessengerPricePreview> = {}): MessengerPricePreview => ({
  transport: 'Max', currentPricePerMonth: null, newPricePerMonth: 590, currentIsActive: true, newIsActive: true, sellableBefore: false, sellableAfter: true,
  startsSelling: true, stopsSelling: false, isZeroPrice: false, activePaidAccounts: 0,
  consequences: ['MAX начнёт продаваться: цена появится на странице тарифов и в мастере подключения'],
  confirmTitle: 'Изменить цену MAX?', confirmButtonText: 'Сохранить', ...o,
})

function renderPanel() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={qc}><MessengerOptionsPanel /></QueryClientProvider>)
}

beforeEach(() => {
  messengerOptions.mockReset(); previewMessengerPrice.mockReset(); updateMessengerPrice.mockReset()
  messengerOptions.mockResolvedValue(catalog())
})

describe('MessengerOptionsPanel (cycle 43, US-43-01)', () => {
  it('shows a skeleton while loading and an error with a retry when the request fails', async () => {
    messengerOptions.mockRejectedValueOnce(new Error('boom'))
    renderPanel()
    expect(screen.getByLabelText('Загрузка цен мессенджеров')).toBeInTheDocument()
    expect(await screen.findByText('Не удалось загрузить цены мессенджеров.')).toBeInTheDocument()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Повторить' }))
    expect(await screen.findByText('Цена мессенджеров')).toBeInTheDocument()
  })

  it('prints both options with the server verdict, the price, the paid accounts and the journal', async () => {
    renderPanel()
    expect(await screen.findByText('Цена действует для всех аккаунтов и всех линеек.')).toBeInTheDocument()
    expect(screen.getByText('Не продаётся: цена не задана')).toBeInTheDocument()
    expect(screen.getByText('Не продаётся: закрыт для владельцев')).toBeInTheDocument()
    expect(screen.getByText(/^490 ₽\/мес/, { selector: 'p' })).toBeInTheDocument()
    expect(screen.getByText('Аккаунтов с действующей оплатой: 3')).toBeInTheDocument()
    expect(screen.getByLabelText('Журнал цены WhatsApp')).toBeInTheDocument()
    expect(screen.getByText(/Иван: 390 ₽\/мес → 490 ₽\/мес · индексация/)).toBeInTheDocument()
  })

  it('requires a reason and a valid price before asking the server for consequences', async () => {
    const user = userEvent.setup()
    renderPanel()
    await user.click(await screen.findByRole('button', { name: 'Изменить цену MAX' }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByLabelText(/Цена за месяц/), '5,999')
    await user.click(within(dialog).getByRole('button', { name: 'Продолжить' }))
    expect(within(dialog).getByText('Цена — не больше двух знаков после запятой')).toBeInTheDocument()
    expect(within(dialog).getByText('Укажите причину изменения цены')).toBeInTheDocument()
    expect(previewMessengerPrice).not.toHaveBeenCalled()
  })

  it('shows the server consequences before saving, then saves with the expected old values and the trimmed reason', async () => {
    const user = userEvent.setup()
    previewMessengerPrice.mockResolvedValue(preview())
    updateMessengerPrice.mockResolvedValue(max({ pricePerMonth: 590, priceText: '590 ₽/мес', sale: { sellable: true, reason: null, text: 'Продаётся' } }))
    renderPanel()
    await user.click(await screen.findByRole('button', { name: 'Изменить цену MAX' }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByLabelText(/Цена за месяц/), '590')
    await user.type(within(dialog).getByLabelText('Причина изменения'), '  запуск продаж ')
    await user.click(within(dialog).getByRole('button', { name: 'Продолжить' }))

    expect(await within(dialog).findByText('Изменить цену MAX?')).toBeInTheDocument()
    expect(within(dialog).getByText(/MAX начнёт продаваться/)).toBeInTheDocument()
    expect(previewMessengerPrice).toHaveBeenCalledWith('Max', { pricePerMonth: 590, isActive: null })
    expect(updateMessengerPrice).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(updateMessengerPrice).toHaveBeenCalledWith('Max', {
      pricePerMonth: 590, isActive: null, comment: 'запуск продаж', expectedPricePerMonth: null, expectedIsActive: true,
    }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('highlights the zero-price warning and prints a 409 from the save verbatim, keeping the dialog open', async () => {
    const user = userEvent.setup()
    previewMessengerPrice.mockResolvedValue(preview({ newPricePerMonth: 0, isZeroPrice: true, consequences: ['Цена 0 ₽: MAX будет подключаться бесплатно'] }))
    updateMessengerPrice.mockRejectedValue({ response: { status: 409, data: 'Цену MAX уже изменил другой администратор. Обновите страницу и проверьте последствия ещё раз' } })
    renderPanel()
    await user.click(await screen.findByRole('button', { name: 'Изменить цену MAX' }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByLabelText(/Цена за месяц/), '0')
    await user.type(within(dialog).getByLabelText('Причина изменения'), 'бесплатно')
    await user.click(within(dialog).getByRole('button', { name: 'Продолжить' }))
    expect(await within(dialog).findByText('Цена 0 ₽: MAX будет подключаться бесплатно')).toHaveClass('font-medium')
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Цену MAX уже изменил другой администратор. Обновите страницу и проверьте последствия ещё раз')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('prints a 400 of the preview (nothing changed) verbatim on the form step', async () => {
    const user = userEvent.setup()
    previewMessengerPrice.mockRejectedValue({ response: { status: 400, data: 'Цена и активность не изменились — сохранять нечего' } })
    renderPanel()
    await user.click(await screen.findByRole('button', { name: 'Изменить цену WhatsApp' }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByLabelText('Причина изменения'), 'x')
    await user.click(within(dialog).getByRole('button', { name: 'Продолжить' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Цена и активность не изменились — сохранять нечего')
  })
})
