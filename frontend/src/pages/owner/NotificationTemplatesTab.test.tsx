import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { NotificationTemplatesTab } from './NotificationTemplatesTab'
import type { NotificationTemplatesResponse } from '../../types'

const getTemplates = vi.fn()
const updateTemplate = vi.fn()
const getText = vi.fn()

vi.mock('../../api/notifications', () => ({
  notificationsApi: {
    getTemplates: (...args: unknown[]) => getTemplates(...args),
    updateTemplate: (...args: unknown[]) => updateTemplate(...args),
    previewTemplate: vi.fn(),
  },
}))
vi.mock('../../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
}))

function templates(overrides: Partial<NotificationTemplatesResponse> = {}): NotificationTemplatesResponse {
  return {
    placeholders: [],
    unsubscribeLine: 'Отписаться: ezbook.ru/u/xyz',
    templates: [
      { type: 'Reminder', body: 'Напоминаем о записи завтра.', isDefault: false, defaultBody: 'default', updatedAt: '2026-09-01T00:00:00Z' },
    ],
    adMarkers: ['скидк', 'акци', '%'],
    warningTextKey: 'TemplateAdWarning',
    warningVersion: '2026-09-21',
    ...overrides,
  }
}

const WARNING_HTML = `<p>Meta.</p>
<h2>Текст у поля ввода (виден всегда)</h2>
<p>Сообщение должно оставаться сервисным.</p>
<h2>Усиленное предупреждение (если в тексте найдены рекламные признаки)</h2>
<p>Похоже, в тексте есть реклама.</p>
<h2>Подтверждение при сохранении</h2>
<p>Сохраняя шаблон, я подтверждаю, что текст сервисный.</p>
<h2>Справка: что можно и чего нельзя</h2>
<p>Можно: имя клиента. Нельзя: скидки.</p>`

function renderTab() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <NotificationTemplatesTab companyId="c1" />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getTemplates.mockReset()
  updateTemplate.mockReset()
  getText.mockReset()
  getText.mockResolvedValue({ key: 'TemplateAdWarning', version: '2026-09-21', isDraft: true, contentHtml: WARNING_HTML })
})

describe('NotificationTemplatesTab', () => {
  it('highlights ad markers found in the current text, using the server-provided dictionary', async () => {
    const user = userEvent.setup()
    getTemplates.mockResolvedValue(templates())
    renderTab()

    const textarea = await screen.findByDisplayValue('Напоминаем о записи завтра.')
    await user.type(textarea, ' Скидка 20%!')

    expect(await screen.findByText(/Похоже на рекламу: скидк, %/)).toBeInTheDocument()
  })

  it('saving opens the acknowledgement modal and requires the checkbox before the API is called', async () => {
    const user = userEvent.setup()
    getTemplates.mockResolvedValue(templates())
    renderTab()

    const textarea = await screen.findByDisplayValue('Напоминаем о записи завтра.')
    await user.type(textarea, ' Ждём вас!')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    expect(await screen.findByText('Подтвердите сохранение шаблона')).toBeInTheDocument()
    expect(updateTemplate).not.toHaveBeenCalled()
  })

  it('confirming sends the acknowledgement with the warningVersion from the templates response', async () => {
    const user = userEvent.setup()
    getTemplates.mockResolvedValue(templates())
    updateTemplate.mockResolvedValueOnce({
      type: 'Reminder',
      body: 'Ждём вас!',
      isDefault: false,
      defaultBody: 'default',
      updatedAt: '2026-09-22T00:00:00Z',
    })
    renderTab()

    const textarea = await screen.findByDisplayValue('Напоминаем о записи завтра.')
    await user.clear(textarea)
    await user.type(textarea, 'Ждём вас!')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await user.click(await screen.findByRole('checkbox', { name: 'Подтверждаю' }))
    await user.click(screen.getAllByRole('button', { name: 'Сохранить' })[1])

    await waitFor(() =>
      expect(updateTemplate).toHaveBeenCalledWith('c1', 'Reminder', 'Ждём вас!', {
        warningVersion: '2026-09-21',
        accepted: true,
        confirmedDespiteMarkers: false,
      }),
    )
  })

  it('when a marker is present, both checkboxes are required and the server call carries confirmedDespiteMarkers: true', async () => {
    const user = userEvent.setup()
    getTemplates.mockResolvedValue(templates())
    updateTemplate.mockResolvedValueOnce({
      type: 'Reminder',
      body: 'Скидка 20%!',
      isDefault: false,
      defaultBody: 'default',
      updatedAt: '2026-09-22T00:00:00Z',
    })
    renderTab()

    const textarea = await screen.findByDisplayValue('Напоминаем о записи завтра.')
    await user.clear(textarea)
    await user.type(textarea, 'Скидка 20%!')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    await user.click(await screen.findByRole('checkbox', { name: 'Подтверждаю' }))
    await user.click(screen.getByRole('checkbox', { name: /Всё равно сохранить/ }))
    await user.click(screen.getAllByRole('button', { name: 'Сохранить' })[1])

    await waitFor(() =>
      expect(updateTemplate).toHaveBeenCalledWith(
        'c1',
        'Reminder',
        'Скидка 20%!',
        expect.objectContaining({ confirmedDespiteMarkers: true }),
      ),
    )
  })

  it('surfaces the JSON markersHit 400 body inside the modal instead of a bare failure', async () => {
    const user = userEvent.setup()
    getTemplates.mockResolvedValue(templates())
    updateTemplate.mockRejectedValueOnce({
      isAxiosError: true,
      response: {
        status: 400,
        data: { markersHit: ['скидк'], message: 'Такой текст с высокой вероятностью является рекламой.' },
      },
    })
    renderTab()

    const textarea = await screen.findByDisplayValue('Напоминаем о записи завтра.')
    await user.clear(textarea)
    await user.type(textarea, 'Текст без явных маркеров')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await user.click(await screen.findByRole('checkbox', { name: 'Подтверждаю' }))
    await user.click(screen.getAllByRole('button', { name: 'Сохранить' })[1])

    expect(await screen.findByText('Такой текст с высокой вероятностью является рекламой.')).toBeInTheDocument()
  })
})
