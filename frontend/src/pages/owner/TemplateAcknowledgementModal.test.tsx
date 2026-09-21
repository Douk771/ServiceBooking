import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TemplateAcknowledgementModal } from './TemplateAcknowledgementModal'

describe('TemplateAcknowledgementModal', () => {
  it('without markers hit, only the base checkbox is required', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    render(
      <TemplateAcknowledgementModal
        confirmationHtml={null}
        heightenedHtml={null}
        markersHit={[]}
        loading={false}
        onConfirm={onConfirm}
        onClose={() => {}}
      />,
    )

    const save = screen.getByRole('button', { name: 'Сохранить' })
    expect(save).toBeDisabled()

    await user.click(screen.getByRole('checkbox', { name: 'Подтверждаю' }))
    expect(save).not.toBeDisabled()

    await user.click(save)
    expect(onConfirm).toHaveBeenCalledWith(false)
  })

  it('with markers hit, BOTH checkboxes are required — the base one alone does not unlock save', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    render(
      <TemplateAcknowledgementModal
        confirmationHtml={null}
        heightenedHtml={null}
        markersHit={['скидк', '%']}
        loading={false}
        onConfirm={onConfirm}
        onClose={() => {}}
      />,
    )

    expect(screen.getByText(/скидк, %/)).toBeInTheDocument()

    const save = screen.getByRole('button', { name: 'Сохранить' })
    await user.click(screen.getByRole('checkbox', { name: 'Подтверждаю' }))
    expect(save).toBeDisabled()

    await user.click(screen.getByRole('checkbox', { name: /Всё равно сохранить/ }))
    expect(save).not.toBeDisabled()

    await user.click(save)
    expect(onConfirm).toHaveBeenCalledWith(true)
  })

  it('shows a server-provided error without losing the checked state', async () => {
    const user = userEvent.setup()
    render(
      <TemplateAcknowledgementModal
        confirmationHtml={null}
        heightenedHtml={null}
        markersHit={[]}
        loading={false}
        error="Такой текст с высокой вероятностью является рекламой."
        onConfirm={() => {}}
        onClose={() => {}}
      />,
    )

    expect(screen.getByText('Такой текст с высокой вероятностью является рекламой.')).toBeInTheDocument()
    await user.click(screen.getByRole('checkbox', { name: 'Подтверждаю' }))
    expect(screen.getByRole('checkbox', { name: 'Подтверждаю' })).toBeChecked()
  })
})
