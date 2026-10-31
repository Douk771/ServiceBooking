import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ContactsPage } from './ContactsPage'
import { PaymentInfoPage } from './PaymentInfoPage'
import { Footer } from '@/components/layout/Footer'

// QA цикла 48: независимые сценарии по критериям SPEC.md US-48-01..03, 06. Значения и фразы продублированы литералами
// (из SPEC.md §4/§11 и LEGAL_REVIEW_CYCLE48.md), а не берутся из contracts/cycle48 и operator-public.json:
// иначе тест повторил бы любую опечатку источника. QA-48-xx — номера в TEST_CATALOG.md.

const FIO = 'Коломеец Илья Константинович'
const INN = '222410420811'
const SITES = ['zapis', 'goods', 'dom', 'bani'] as const

let fetchSpy: ReturnType<typeof vi.fn>
beforeEach(() => {
  fetchSpy = vi.fn(() => Promise.reject(new Error('network is forbidden on static pages')))
  vi.stubGlobal('fetch', fetchSpy)
})
afterEach(() => vi.unstubAllGlobals())

const mount = (ui: React.ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>)

describe.each(SITES)('QA-48-01 /contacts на %s (US-48-02)', (site) => {
  it('один h1, main, title, все сведения об операторе, tel/mailto, без сети', () => {
    mount(<ContactsPage site={site} />)
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Контакты и реквизиты')
    expect(screen.getByRole('main')).toBeInTheDocument()
    expect(document.title).toContain('Контакты и реквизиты')
    const text = document.body.textContent ?? ''
    expect(text).toContain(FIO)
    expect(text).toContain(INN)
    expect(text).toContain('Налог на профессиональный доход')
    expect(text).toContain('не зарегистрированное в качестве индивидуального предпринимателя')
    expect(text).toContain('ОГРН')
    expect(text).toContain('не присваивался')
    expect(text).toContain('656049, Алтайский край, г. Барнаул, Красноармейский проспект, д. 61Б, кв. 113')
    expect(text).toContain('10 рабочих дней')
    expect(text).toContain('по рабочим дням с 10:00 до 18:00 по барнаульскому времени (МСК+4)')
    expect(screen.getByRole('link', { name: '+7 913 225 89 15' })).toHaveAttribute('href', 'tel:+79132258915')
    expect(screen.getByRole('link', { name: 'ezbook.privacy@yandex.ru' })).toHaveAttribute('href', 'mailto:ezbook.privacy@yandex.ru')
    expect(screen.getAllByRole('link', { name: 'ezbook.privacy@yandex.ru' })[0]).toHaveAttribute('href', 'mailto:ezbook.privacy@yandex.ru')
    expect(screen.getByRole('link', { name: 'форму обращения' })).toHaveAttribute('href', '/data-request')
    expect(screen.getByRole('link', { name: 'Соглашение с компанией' })).toHaveAttribute('href', '/terms-owner')
    expect(screen.getByRole('link', { name: 'Политика обработки персональных данных' })).toHaveAttribute('href', '/privacy')
    expect(document.body.innerHTML).not.toContain('{{')
    expect(fetchSpy).not.toHaveBeenCalled()
  })

  it('«Тарифы и цены» есть только на сайтах, где есть публичная /pricing', () => {
    mount(<ContactsPage site={site} />)
    const pricing = screen.queryByRole('link', { name: 'Тарифы и цены' })
    if (site === 'zapis' || site === 'goods') expect(pricing).toHaveAttribute('href', '/pricing')
    else expect(pricing).toBeNull()
  })

  it('ezbook.privacy@ подписан как адрес официальных обращений (включая отказ и возврат), а не «только для ПДн»', () => {
    mount(<ContactsPage site={site} />)
    expect(document.body.textContent).toMatch(/E-mail:.*официальные обращения: претензии, отказ от договора и возврат денег/)
    expect(document.body.textContent).not.toMatch(/только для (обращений по )?(персональн|ПДн)/i)
  })
})

describe.each(SITES)('QA-48-02 /payment-info на %s (US-48-03)', (site) => {
  it('один h1, main, 6 разделов, без сети, значения подставлены', () => {
    mount(<PaymentInfoPage site={site} />)
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Оплата и возврат')
    expect(screen.getByRole('main')).toBeInTheDocument()
    expect(document.title).toContain('Оплата и возврат')
    expect(screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)).toEqual([
      '1. Как подключить тариф и когда он включается',
      '2. Как оплатить',
      '3. Что вы оплачиваете и на какой срок',
      '4. Отказ от договора и возврат денег',
      '5. Налоги и чек',
      '6. Какой документ главнее',
    ])
    const text = document.body.textContent ?? ''
    expect(text).toContain(INN)
    expect(text).toContain('не позднее следующего рабочего дня после дня подачи заявки')
    expect(text).toContain('не позднее 2 рабочих дней после дня, когда оплата зачислена на счёт оператора')
    expect(document.body.innerHTML).not.toContain('{{')
    expect(fetchSpy).not.toHaveBeenCalled()
  })

  it('не обещает того, чего нет: оплата картой отсутствует, автопродления нет, об этом сказано прямо', () => {
    mount(<PaymentInfoPage site={site} />)
    const text = document.body.textContent ?? ''
    expect(text).toContain('Оплаты банковской картой в сервисе пока нет')
    expect(text).toContain('Автопродления нет')
    expect(text).not.toMatch(/оплатить картой|оплатите картой|оплата картой доступна|кнопка оплаты|автоматическ\w+ списани\w+ (производятся|будут)/i)
    expect(text).not.toMatch(/мы свяжемся с вами|после заявки/i)
  })

  it('ссылки: документы, контакты, письмо об отказе на оба адреса', () => {
    mount(<PaymentInfoPage site={site} />)
    expect(screen.getAllByRole('link', { name: 'Соглашение с компанией' })[0]).toHaveAttribute('href', '/terms-owner')
    expect(screen.getAllByRole('link', { name: /Приложение № 1/ })[0]).toHaveAttribute('href', '/offer-channel')
    expect(screen.getAllByRole('link', { name: /Контакты и реквизиты/ })[0]).toHaveAttribute('href', '/contacts')
    const mails = screen.getAllByRole('link').map((a) => a.getAttribute('href'))
    expect(mails).toContain('mailto:ezbook.privacy@yandex.ru')
    expect(mails).toContain('mailto:ezbook.privacy@yandex.ru')
    const price = screen.getAllByRole('link', { name: /Тарифы и цены/ })[0]
    // dom/bani своей /pricing не имеют: ссылка абсолютная на сайт «Записи» (в тесте адрес берётся из окружения, в сборке — https://ezbook.ru)
    const href = price.getAttribute('href') ?? ''
    if (site === 'zapis' || site === 'goods') expect(href).toBe('/pricing')
    else expect(href).toMatch(/^https?:\/\/[^/]+\/pricing$/)
  })

  it('пример расчёта возврата арифметически верен: 3 900 ₽ за 30 дней, 10 дней = 1 300 ₽, вернуть 2 600 ₽', () => {
    mount(<PaymentInfoPage site={site} />)
    expect(document.body.textContent).toContain('плата за них — 1 300 ₽, вернём 2 600 ₽')
    expect(3900 / 30 * 10).toBe(1300)
    expect(3900 - 1300).toBe(2600)
  })
})

describe('QA-48-03 подвал ezbook (US-48-01)', () => {
  it('строка реквизитов дословно, tel/mailto, новые ссылки, прежние ссылки на месте', () => {
    mount(<Footer />)
    const footer = screen.getByRole('contentinfo')
    expect(footer.textContent).toContain(`${FIO}, самозанятый (плательщик НПД), ИНН ${INN}, г. Барнаул`)
    expect(within(footer).getByRole('link', { name: '+7 913 225 89 15' })).toHaveAttribute('href', 'tel:+79132258915')
    expect(within(footer).getByRole('link', { name: 'ezbook.privacy@yandex.ru' })).toHaveAttribute('href', 'mailto:ezbook.privacy@yandex.ru')
    expect(within(footer).getByRole('link', { name: 'Контакты и реквизиты' })).toHaveAttribute('href', '/contacts')
    expect(within(footer).getByRole('link', { name: 'Оплата и возврат' })).toHaveAttribute('href', '/payment-info')
    expect(within(footer).getByRole('link', { name: 'Политика обработки персональных данных' })).toHaveAttribute('href', '/privacy')
    expect(within(footer).getByRole('link', { name: 'Пользовательское соглашение' })).toHaveAttribute('href', '/terms')
    expect(within(footer).getByRole('link', { name: 'Соглашение с компанией' })).toHaveAttribute('href', '/terms-owner')
    expect(footer.textContent).not.toContain('{{')
  })

  it('ИНН и телефон не разрываются при переносе на 375 px (whitespace-nowrap)', () => {
    mount(<Footer />)
    const footer = screen.getByRole('contentinfo')
    const innEl = Array.from(footer.querySelectorAll('*')).find((e) => e.children.length === 0 && e.textContent?.includes(INN))
    const phone = within(footer).getByRole('link', { name: '+7 913 225 89 15' })
    const holder = (innEl as HTMLElement | undefined) ?? undefined
    expect(holder, 'ИНН выведен отдельным элементом').toBeDefined()
    // ИНН — часть строки целиком: допустим nowrap на самом элементе ИНН либо на его предке-обёртке.
    expect(holder!.closest('[class*="whitespace-nowrap"]') ?? holder!.querySelector('[class*="whitespace-nowrap"]')).not.toBeNull()
    expect(phone.closest('[class*="whitespace-nowrap"]') ?? phone.querySelector('[class*="whitespace-nowrap"]')).not.toBeNull()
  })
})
