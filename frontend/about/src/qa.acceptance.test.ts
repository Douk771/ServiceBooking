import { describe, it, expect } from 'vitest'
import { renderSite } from './prerender'
import { DEMO_SITE_URLS, productsPageHref } from '@/utils/publicSites'

// QA цикла 44: сценарии по SPEC_CYCLE44_PRODUCTS_LANDING.md §5, написаны независимо от реализации (только публичный renderSite).
const TEMPLATE = '<!doctype html><html lang="ru"><head><!--about:head--></head><body><!--about:body--></body></html>'
const prod = renderSite({ template: TEMPLATE, env: {}, mode: 'production', year: 2026 })
const html = prod.files['index.html']
const hrefs = (s: string) => [...s.matchAll(/<a [^>]*href="([^"]*)"/g)].map((m) => m[1])
const actionLinks = (s: string, action: string) =>
  (s.match(new RegExp(`<a [^>]*data-action="${action}"[^>]*>`, 'g')) ?? []).map((a) => /href="([^"]+)"/.exec(a)![1])
const text = (s: string) => s.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ')

describe('CY44-QA US-44-01/03 карточки и ссылки', () => {
  it('четыре карточки по умолчанию, у каждой «для кого», 3-5 пунктов, хост, ссылка клиента', () => {
    const cards = html.split('<article ').slice(1).map((c) => c.slice(0, c.indexOf('</article>')))
    expect(cards).toHaveLength(4)
    for (const c of cards) {
      const items = (c.match(/<li /g) ?? []).length
      expect(items).toBeGreaterThanOrEqual(3)
      expect(items).toBeLessThanOrEqual(5)
      expect(c).toContain('data-role="host"')
      expect(c).toContain('data-action="client"')
      expect(c).toMatch(/<h3 /)
    }
  })
  it('все внешние ссылки абсолютные https, без пустых и без javascript:', () => {
    for (const h of hrefs(html)) {
      expect(h === '' || h.startsWith('javascript:')).toBe(false)
      // Cycle 48: tel:/mailto: of the footer requisites line are not links to other pages.
      if (!h.startsWith('#') && !h.startsWith('/') && !h.startsWith('tel:') && !h.startsWith('mailto:')) expect(h).toMatch(/^https:\/\/[a-z.]*ezbook\.ru\//)
    }
  })
  it('ссылки действий ведут на хост своего продукта', () => {
    expect(html).toContain('href="https://ezbook.ru/"')
    expect(html).toContain('href="https://goods.ezbook.ru/"')
    expect(html).toContain('href="https://dom.ezbook.ru/"')
    expect(html).toContain('href="https://bani.ezbook.ru/"')
    expect(html).toContain('href="https://ezbook.ru/#biz-title"')
    expect(html).toContain('href="https://goods.ezbook.ru/#biz-title"')
    expect(html).toContain('href="https://dom.ezbook.ru/#biz-title"')
    expect(html).toContain('href="https://bani.ezbook.ru/#biz-title"')
  })
  it('у Записи и Заказов есть ссылка на демо-стенд', () => {
    expect(actionLinks(html, 'demo')).toEqual(['https://demo.visit.ezbook.ru/', 'https://demo.zakaz.ezbook.ru/'])
  })
  it('у ссылок нет безымянных текстов', () => {
    expect(html).not.toMatch(/>\s*(тут|здесь|подробнее)\s*</i)
  })
})

describe('CY44-QA US-44-06 окружения', () => {
  it('dev: ссылки на сайты — localhost, ezbook.ru только у демо-стендов', () => {
    const dev = renderSite({ template: TEMPLATE, env: {}, mode: 'development', year: 2026 }).files['index.html']
    // Демо-стенды публичные и одни на все окружения, поэтому их ссылки не локальные — но только на адреса DEMO_SITE_URLS.
    const demos = actionLinks(dev, 'demo')
    expect(demos).toEqual(Object.values(DEMO_SITE_URLS).map((o) => `${o}/`))
    const ext = hrefs(dev).filter((h) => h.startsWith('http') && !demos.includes(h))
    expect(ext.length).toBeGreaterThan(0)
    for (const h of ext) expect(h).toMatch(/^http:\/\/localhost:517\d\//)
  })
  it('переменные окружения переопределяют адреса (тестовый стенд)', () => {
    const t = renderSite({
      template: TEMPLATE,
      env: { VITE_SITE_URL_GOODS: 'https://goods.test.example', VITE_SITE_URL_ABOUT: 'https://about.test.example' },
      mode: 'production',
      year: 2026,
    })
    expect(t.files['index.html']).toContain('href="https://goods.test.example/"')
    expect(t.files['index.html']).toContain('href="https://about.test.example/"')
    expect(t.report.canonical).toBe('https://about.test.example/')
  })
  it('невалидный адрес (http в проде, путь, слэш) роняет сборку', () => {
    for (const bad of ['http://evil.example', 'https://x.example/path', 'https://x.example/', 'not a url']) {
      expect(() => renderSite({ template: TEMPLATE, env: { VITE_SITE_URL_DOM: bad }, mode: 'production', year: 2026 })).toThrow()
    }
  })
})

describe('CY44-QA US-44-07 поиск', () => {
  it('title, description, canonical, lang, один h1, без noindex и скриптов', () => {
    expect(html).toMatch(/<html lang="ru"/)
    expect(html).toMatch(/<title>[^<]+<\/title>/)
    expect(html).toMatch(/<meta name="description" content="[^"]{20,}"/)
    expect(html).toContain('<link rel="canonical" href="https://about.ezbook.ru/"')
    expect((html.match(/<h1[\s>]/g) ?? []).length).toBe(1)
    expect(html).not.toMatch(/noindex|<script|\sstyle=/i)
  })
  it('robots разрешает, sitemap содержит страницу, 404 закрыт noindex', () => {
    expect(prod.files['robots.txt']).toMatch(/Allow: \//)
    expect(prod.files['robots.txt']).not.toMatch(/Disallow: \/\s*$/m)
    expect(prod.files['sitemap.xml']).toContain('<loc>https://about.ezbook.ru/</loc>')
    expect(prod.files['404.html']).toContain('noindex')
  })
})

describe('CY44-QA содержание: запреты и доступность', () => {
  const t = text(html)
  it('нет цен, «бесплатно», превосходных степеней, форм и счётчиков', () => {
    expect(t).not.toMatch(/₽|руб|бесплатн|№\s?1|лучш|самый|самая|самые|без комисси/i)
    expect(html).not.toMatch(/<form|<input|<button|metrika|mc\.yandex|googletagmanager|localStorage|\/api\//i)
  })
  it('по умолчанию (решение 20.10.2026) тестовых карточек нет: у всех есть «Для бизнеса»', () => {
    // Карточка в тестовом режиме без призыва подключаться проверяется на фикстуре в render.test.tsx.
    expect(html).not.toContain('Тестовый режим')
    for (const key of ['zapis', 'goods', 'dom', 'bani']) {
      const rest = html.slice(html.indexOf(`data-product="${key}"`))
      expect(rest.slice(0, rest.indexOf('</article>'))).toContain('data-action="business"')
    }
  })
  it('блок аккаунта не обещает единого входа', () => {
    expect(t).toContain('Один аккаунт на всех сайтах')
    expect(t).not.toMatch(/единый вход|войдите один раз|один раз войд/i)
    expect(t).toMatch(/войти на одном сайте не значит войти на другом/i)
  })
  it('бренд пишется только EZBOOK', () => {
    expect(t).not.toMatch(/ezbook(?!\.ru)|Ezbook|EzBook/)
  })
  it('нет truncate, иконки декоративные, кнопки не ниже 44px, одна колонка на мобильном', () => {
    expect(html).not.toContain('truncate')
    for (const svg of html.match(/<svg [^>]*>/g) ?? []) expect(svg).toContain('aria-hidden="true"')
    for (const a of html.match(/<a [^>]*data-action[^>]*>/g) ?? []) expect(a).toContain('min-h-[44px]')
    expect(html).toMatch(/grid gap-6 lg:grid-cols-2/)
  })
  it('скрытых продуктов по умолчанию нет', () => {
    expect(prod.report.hiddenProducts).toEqual([])
  })
  it('h2 на блок, nav с aria-label', () => {
    expect((html.match(/<h2[\s>]/g) ?? []).length).toBeGreaterThanOrEqual(2)
    expect(html).toMatch(/<nav aria-label="[^"]+"/)
  })
})

describe('CY44-QA US-44-08 ссылка в подвалах: флаг', () => {
  it('по умолчанию в проде ссылки нет (R44-4), с флагом on — есть, в dev — всегда', () => {
    expect(productsPageHref({})).toBeNull()
    expect(productsPageHref({ VITE_PRODUCTS_PAGE_LINK: 'on' })).toBe('https://about.ezbook.ru/')
    expect(productsPageHref({ DEV: true })).toBe('http://localhost:5177/')
    expect(productsPageHref({ VITE_PRODUCTS_PAGE_LINK: 'on', VITE_SITE_URL_ABOUT: 'garbage' })).toBeNull()
  })
})
