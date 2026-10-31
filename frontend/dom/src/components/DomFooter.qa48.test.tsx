import { describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { DomFooter } from './DomFooter'

// QA цикла 48, QA-48-03 (US-48-01): подвал сайта «dom». Значения продублированы литералами из SPEC.md, не из operator-public.json.
describe('QA-48-03 подвал dom (US-48-01)', () => {
  it('строка реквизитов дословно, tel/mailto, новые ссылки на своём домене, прежние ссылки на месте', () => {
    render(
      <MemoryRouter>
        <DomFooter />
      </MemoryRouter>,
    )
    const footer = screen.getByRole('contentinfo')
    expect(footer.textContent).toContain('Коломеец Илья Константинович, самозанятый (плательщик НПД), ИНН 222410420811, г. Барнаул')
    expect(within(footer).getByRole('link', { name: '+7 913 225 89 15' })).toHaveAttribute('href', 'tel:+79132258915')
    expect(within(footer).getByRole('link', { name: 'ezbook.privacy@yandex.ru' })).toHaveAttribute('href', 'mailto:ezbook.privacy@yandex.ru')
    expect(within(footer).getByRole('link', { name: 'Контакты и реквизиты' })).toHaveAttribute('href', '/contacts')
    expect(within(footer).getByRole('link', { name: 'Оплата и возврат' })).toHaveAttribute('href', '/payment-info')
    expect(within(footer).getByRole('link', { name: 'Политика обработки персональных данных' })).toHaveAttribute('href', '/privacy')
    expect(within(footer).getByRole('link', { name: 'Пользовательское соглашение' })).toHaveAttribute('href', '/terms')
    expect(footer.textContent).not.toContain('{{')
    const phone = within(footer).getByRole('link', { name: '+7 913 225 89 15' })
    expect(phone.closest('[class*="whitespace-nowrap"]') ?? phone.querySelector('[class*="whitespace-nowrap"]')).not.toBeNull()
  })
})
