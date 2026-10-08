import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ServicePickDialog } from './ServicePickDialog'

// ЮР39-6 / 69-ФЗ: nothing is pre-selected, even when the company has a single service.
describe('ServicePickDialog', () => {
  it('does not pre-select the only service: the guest picks it explicitly and the time picker is not shown yet', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <ServicePickDialog
          title="Добавить"
          services={[{ id: 's1', name: 'Баня' }]}
          loadStarts={vi.fn()}
          loadQuote={vi.fn()}
          confirmLabel="Добавить"
          pending={false}
          error=""
          onConfirm={vi.fn()}
          onClose={vi.fn()}
        />
      </QueryClientProvider>,
    )
    const radio = screen.getByRole('radio', { name: 'Баня' })
    expect(radio).not.toBeChecked()
    expect(screen.queryByTestId('service-picker')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Добавить' })).toBeDisabled()
  })
})
