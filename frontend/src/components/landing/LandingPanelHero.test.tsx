import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ServiceLanding } from './ServiceLanding'
import { makeConfig, makePanelConfig } from './testConfig'
import { DEFAULT_BRANCH_HTML } from './landingReference'
import type { LandingConfig, LandingPanelConfig } from './types'

const aside = (
  <form aria-label="Панель">
    <input aria-label="Поле" />
  </form>
)
function renderPanel(config: LandingPanelConfig = makePanelConfig()) {
  return render(
    <MemoryRouter>
      <ServiceLanding config={config} heroAside={aside} catalog={<p>каталог</p>} />
    </MemoryRouter>,
  )
}
const before = (a: Element, b: Element) => Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING)

describe('LandingPanelHero (T41-01)', () => {
  it('a) order, one main, one h1', () => {
    const { container } = renderPanel()
    expect(container.querySelectorAll('main')).toHaveLength(1)
    expect(container.querySelectorAll('h1')).toHaveLength(1)
    const els = [
      container.querySelector('h1')!,
      container.querySelector('form[aria-label="Панель"]')!,
      ...['cat', 'c-title', 'b-title', 'faq'].map((id) => container.querySelector(`#${id}`)!),
    ]
    els.forEach((e) => expect(e).toBeTruthy())
    for (let i = 0; i < els.length - 1; i++) expect(before(els[i], els[i + 1])).toBe(true)
  })

  it('b) slot inside the panel frame, frame inside the hero strip, before catalog', () => {
    const { container } = renderPanel()
    const form = container.querySelector('form')!
    const frame = form.parentElement!
    expect(frame.className).toContain('rounded-3xl')
    const strip = container.querySelector('main')!.firstElementChild!
    expect(strip.contains(frame)).toBe(true)
    expect(strip.contains(container.querySelector('#cat'))).toBe(false)
    expect(before(frame, container.querySelector('#cat')!)).toBe(true)
  })

  it('c) main is full-width, sections wrapper has the container', () => {
    const { container } = renderPanel()
    const main = container.querySelector('main')!
    expect(main.className).not.toContain('max-w-[1180px]')
    expect(container.querySelector('#cat')!.parentElement!.className).toContain('max-w-[1180px]')
  })

  it('d) mountains backdrop is one decorative svg; none has no backdrop', () => {
    const { container, unmount } = renderPanel()
    const svgs = container.querySelectorAll('svg[aria-hidden="true"][focusable="false"]')
    expect(svgs).toHaveLength(1)
    expect(svgs[0].querySelector('title')).toBeNull()
    expect(svgs[0].textContent).toBe('')
    unmount()
    const base = makePanelConfig()
    const none = renderPanel({ ...base, hero: { ...base.hero, backdrop: 'none' } })
    expect(none.container.querySelectorAll('svg[focusable="false"]')).toHaveLength(0)
  })

  it('e) facts list: 3 items with hidden icons; 0 facts has no list', () => {
    const { container, unmount } = renderPanel()
    const items = container.querySelectorAll('main ul[role="list"]')[0].querySelectorAll('li')
    expect(items).toHaveLength(3)
    items.forEach((li) => expect(li.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true'))
    unmount()
    const base = makePanelConfig()
    const none = renderPanel({ ...base, hero: { ...base.hero, facts: [] } })
    expect(none.container.querySelector('main > div:first-child ul')).toBeNull()
  })

  it('f) h1 classes match the default hero; anchor is href + 44px', () => {
    const def = render(
      <MemoryRouter>
        <ServiceLanding config={makeConfig()} catalog={<p />} />
      </MemoryRouter>,
    )
    const defClass = def.container.querySelector('h1')!.className
    def.unmount()
    const { container } = renderPanel()
    expect(container.querySelector('h1')!.className).toBe(defClass)
    const a = container.querySelector('a[href="#c-title"]')!
    expect(a.className).toContain('min-h-[44px]')
  })

  it('g) panel config without heroAside does not crash and has no frame', () => {
    const props = { config: makePanelConfig(), catalog: <p>к</p> } as never
    const { container } = render(
      <MemoryRouter>
        <ServiceLanding {...(props as object as { config: LandingPanelConfig; catalog: null; heroAside: never })} />
      </MemoryRouter>,
    )
    expect(container.querySelector('h1')).toBeTruthy()
    expect(container.querySelector('.rounded-3xl')).toBeNull()
  })

  it('h) Tab order: anchor, then first slot field', async () => {
    renderPanel()
    const user = userEvent.setup()
    await user.tab()
    expect(document.activeElement!.getAttribute('href')).toBe('#c-title')
    await user.tab()
    expect(document.activeElement!.getAttribute('aria-label')).toBe('Поле')
  })
})

describe('type guards (T41-01t)', () => {
  it('compile-time only', () => {
    const noop = (_: unknown) => undefined
    noop(() => {
      // @ts-expect-error панельный конфиг без heroAside
      void (<ServiceLanding config={makePanelConfig()} catalog={null} />)
      // @ts-expect-error heroAside={null}
      void (<ServiceLanding config={makePanelConfig()} catalog={null} heroAside={null} />)
      // @ts-expect-error обычный конфиг с heroAside
      void (<ServiceLanding config={makeConfig()} catalog={null} heroAside={aside} />)
      const base = makePanelConfig()
      // @ts-expect-error 4 факта
      const four: LandingPanelConfig = { ...base, hero: { ...base.hero, facts: [base.hero.facts[0], base.hero.facts[0], base.hero.facts[0], base.hero.facts[0]] } }
      // @ts-expect-error backdrop вне списка
      const photo: LandingPanelConfig = { ...base, hero: { ...base.hero, backdrop: 'photo' } }
      // @ts-expect-error primaryAction в панельной шапке
      const withCta: LandingPanelConfig = { ...base, hero: { ...base.hero, primaryAction: base.hero.howTo } }
      // @ts-expect-error панельный конфиг не LandingConfig
      const plain: LandingConfig = base
      void [four, photo, withCta, plain]
    })
  })
})

describe('default branch regression (T41-02)', () => {
  it('main class literal and structure unchanged', () => {
    const { container } = render(
      <MemoryRouter>
        <ServiceLanding config={makeConfig()} catalog={<p>cat</p>} />
      </MemoryRouter>,
    )
    const main = container.querySelector('main')!
    expect(main.className).toBe('max-w-[1180px] mx-auto px-4 sm:px-8 pt-10 md:pt-16 pb-10')
    const first = main.firstElementChild!
    expect(first.className).toContain('max-w-[720px]')
    expect(first.querySelector('h1')).toBeTruthy()
    expect(container.querySelector('svg[aria-hidden="true"][focusable="false"]')).toBeNull()
    expect(container.querySelector('.rounded-3xl')).toBeNull()
  })

  it('innerHTML equals the reference captured before the cycle-41 change', () => {
    const { container } = render(
      <MemoryRouter>
        <ServiceLanding config={makeConfig()} catalog={<p>cat</p>} />
      </MemoryRouter>,
    )
    // useId зависит от числа предыдущих рендеров в файле, поэтому счётчик в id приводится к одному виду.
    const norm = (h: string) => h.replace(/:r[0-9a-z]+:/g, ':rN:')
    expect(norm(container.innerHTML)).toBe(norm(DEFAULT_BRANCH_HTML))
  })
})
