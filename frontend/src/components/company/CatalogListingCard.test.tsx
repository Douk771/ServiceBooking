import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CatalogListingCard, type CatalogListingView } from './CatalogListingCard'

const view = (over: Partial<CatalogListingView> = {}): CatalogListingView => ({
  showInCatalog: true,
  allowedByPlan: true,
  visible: true,
  statusText: 'Салон виден в каталоге ezbook.ru',
  notAllowedByPlanText: null,
  checklist: [{ code: 'HiddenByOwner', text: 'Показ включен в настройках', done: true }],
  ...over,
})

function renderCard(props: Partial<React.ComponentProps<typeof CatalogListingCard>> = {}) {
  const onToggle = vi.fn()
  const onRetry = vi.fn()
  render(
    <CatalogListingCard
      title="Каталог ezbook.ru"
      switchLabel="Показывать салон в каталоге ezbook.ru"
      headingId="h"
      headingAs="h3"
      data={view()}
      isLoading={false}
      onRetry={onRetry}
      saving={false}
      onToggle={onToggle}
      {...props}
    />,
  )
  return { onToggle, onRetry }
}

describe('CatalogListingCard', () => {
  it('the switch is named by switchLabel, checked from the server value, and toggles', async () => {
    const { onToggle } = renderCard()
    const sw = screen.getByRole('switch', { name: 'Показывать салон в каталоге ezbook.ru' })
    expect(sw).toBeChecked()
    await userEvent.click(sw)
    expect(onToggle).toHaveBeenCalledWith(false)
    expect(screen.getByRole('heading', { level: 3, name: 'Каталог ezbook.ru' })).toBeInTheDocument()
  })

  it('checklist shows ✓/✗ hidden from readers and an sr-only state', () => {
    renderCard({
      data: view({ checklist: [{ code: 'A', text: 'Первое', done: true }, { code: 'B', text: 'Второе', done: false }] }),
    })
    const checks = screen.getAllByTestId('listing-check')
    expect(checks[0]).toHaveAttribute('data-done', 'true')
    expect(checks[0]).toHaveTextContent('— выполнено')
    expect(checks[1]).toHaveTextContent('— не выполнено')
    expect(screen.getByTestId('listing-status')).toHaveTextContent('Салон виден')
  })

  it('a plan that does not allow listing disables the switch and prints the hint', () => {
    renderCard({ data: view({ allowedByPlan: false, showInCatalog: false, notAllowedByPlanText: 'Не входит в тариф' }) })
    expect(screen.getByRole('switch')).toBeDisabled()
    expect(screen.getByTestId('listing-not-allowed')).toHaveTextContent('Не входит в тариф')
  })

  it('saving disables the switch', () => {
    renderCard({ saving: true })
    expect(screen.getByRole('switch')).toBeDisabled()
  })

  it('a load error shows the text and "Повторить" calls onRetry', async () => {
    const { onRetry } = renderCard({ data: undefined, loadError: 'Не удалось загрузить' })
    expect(screen.getByRole('alert')).toHaveTextContent('Не удалось загрузить')
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }))
    expect(onRetry).toHaveBeenCalled()
  })

  it('loading shows a skeleton, no switch', () => {
    renderCard({ data: undefined, isLoading: true })
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'Загрузка' })).toBeInTheDocument()
  })

  it('a save error is shown', () => {
    renderCard({ saveError: 'Не удалось сохранить' })
    expect(screen.getByRole('alert')).toHaveTextContent('Не удалось сохранить')
  })

  it('cycle 32: headingClassName is applied; without it the goods style stays', () => {
    renderCard({ headingAs: 'h2', headingClassName: 'text-[15px] font-semibold text-ink' })
    expect(screen.getByRole('heading', { level: 2, name: 'Каталог ezbook.ru' })).toHaveClass('text-[15px]')
  })

  it('cycle 32: default heading class is the serif title', () => {
    renderCard()
    expect(screen.getByRole('heading', { name: 'Каталог ezbook.ru' })).toHaveClass('font-serif', 'text-xl')
  })

  it('CY49-F1: marks the unmet item with ✗ and the met one with ✓', () => {
    renderCard({
      data: view({ checklist: [{ code: 'A', text: 'Первое', done: true }, { code: 'B', text: 'Второе', done: false }] }),
    })
    const checks = screen.getAllByTestId('listing-check')
    expect(checks[0]).toHaveTextContent('✓')
    expect(checks[1]).toHaveTextContent('✗')
    expect(checks[1]).not.toHaveTextContent('○')
    expect(checks[1]).toHaveTextContent('— не выполнено')
  })

  it('CY49-F1: prints conditionsText between the status and the checklist, and nothing when it is absent or null', () => {
    renderCard({ data: view({ conditionsText: 'Нужны все условия.' }) })
    const cond = screen.getByTestId('listing-conditions')
    expect(cond).toHaveTextContent('Нужны все условия.')
    const status = screen.getByTestId('listing-status')
    const list = screen.getByRole('list')
    expect(status.compareDocumentPosition(cond) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(cond.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('CY49-F1: no conditions paragraph for null or missing text (shop)', () => {
    const { unmount } = render(
      <CatalogListingCard title="t" switchLabel="s" headingId="h1" data={view({ conditionsText: null })} isLoading={false} onRetry={vi.fn()} saving={false} onToggle={vi.fn()} />,
    )
    expect(screen.queryByTestId('listing-conditions')).not.toBeInTheDocument()
    unmount()
    renderCard()
    expect(screen.queryByTestId('listing-conditions')).not.toBeInTheDocument()
  })
})
