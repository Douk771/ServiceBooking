import { describe, it, expect } from 'vitest'
import contract from '../../../../contracts/cycle46/zapis-landing.json'
import catalogSource from './ZapisCompanyCatalog.tsx?raw'
import { collectConfigStrings } from '../../components/landing/testConfig'
import { zapisLanding } from './zapisLanding'
import { zapisFaq } from './zapisFaq'
import { fillOperatorTemplate } from '../../utils/operatorRequisites'
import storefrontPages from '../../../../contracts/cycle48/storefront-pages.json'
import { bookingFlowCarousel, bookingsShot } from '../../assets/screenshots/zapis/shots'

// Тексты «Записи» окончательные (цикл 46, П5): конфиг обязан совпадать с contracts/cycle46/zapis-landing.json.

const strip = (o: unknown): unknown => JSON.parse(JSON.stringify(o))

describe('zapisLanding = contracts/cycle46/zapis-landing.json (CY46-08)', () => {
  it('hero, catalog frame, business and steps are deeply equal to the contract', () => {
    expect(strip(zapisLanding.hero)).toEqual(contract.hero)
    expect(zapisLanding.order).toBe('business-first')
    expect(zapisLanding.catalog).toEqual({ id: contract.catalog.id, ariaLabel: contract.catalog.ariaLabel })
    const { screenshot, stepsPanel, ...business } = zapisLanding.business
    expect(strip(business)).toEqual({
      eyebrow: contract.business.eyebrow,
      title: contract.business.title,
      titleId: contract.business.titleId,
      text: contract.business.text,
      actions: contract.business.actions,
      benefits: contract.business.benefits,
    })
    expect(screenshot).toBe(bookingsShot)
    expect(screenshot?.caption).toBe(contract.business.screenshot.caption)
    expect(strip(stepsPanel)).toEqual({ title: contract.steps.title, titleId: contract.steps.titleId, steps: contract.steps.items })
  })

  it('clients and pricing lead are equal to the contract; carousel label matches', () => {
    const { screenshot, ...clients } = zapisLanding.clients
    expect(strip(clients)).toEqual({
      eyebrow: contract.clients.eyebrow,
      title: contract.clients.title,
      titleId: contract.clients.titleId,
      text: contract.clients.text,
      list: contract.clients.list,
    })
    expect(screenshot).toBe(bookingFlowCarousel)
    expect(bookingFlowCarousel.label).toBe(contract.clients.screenshot.label)
    expect(zapisLanding.pricing?.lead).toBe(contract.pricing.lead)
  })

  it('FAQ is deeply equal to the contract (order and wording)', () => {
    // Cycle 48 (CY48-08): the contract keeps the first answer as a template; the config fills in the operator values.
    const filled = contract.faq.map((f) => ({ ...f, answer: fillOperatorTemplate(f.answer) }))
    expect(strip(zapisFaq)).toEqual(filled)
  })

  it('CY48-08: contract version 48.0.0; the first answer is the storefront-pages.json template', () => {
    expect(contract.version).toBe('48.0.0')
    expect(contract.faq[0].answer).toBe(storefrontPages.faq.zapisPriceAnswer)
    expect(zapisFaq[0].answer).toBe(fillOperatorTemplate(storefrontPages.faq.zapisPriceAnswer))
    expect(zapisFaq[0].answer).not.toContain('{{')
  })

  it('catalog texts in the component source are the contract texts', () => {
    for (const t of [contract.catalog.title, contract.catalog.subtitle, contract.catalog.searchPlaceholder, contract.catalog.empty, contract.catalog.error]) {
      expect(catalogSource).toContain(t)
    }
  })

  it('hero primary action behaves like the first business action (CY46-04)', () => {
    expect(zapisLanding.hero.primaryAction).toEqual(zapisLanding.business.actions[0])
  })

  it('hero client link leads to the catalog; no how-to link in the first screen (CY46-01)', () => {
    expect(zapisLanding.hero.clientLink.action.href).toBe(`#${zapisLanding.catalog.id}`)
    expect(zapisLanding.hero.howTo).toBeUndefined()
  })
})

describe('forbidden wording (CY46-06)', () => {
  const rules = contract.rules
  const heroStrings = collectConfigStrings(zapisLanding.hero).text
  const faqStrings = zapisFaq.flatMap((f) => [f.question, ...(typeof f.answer === 'string' ? [f.answer] : f.answer), f.link?.label ?? ''])
  const all = [...collectConfigStrings(zapisLanding).text, ...faqStrings, catalogSource]

  it.each(rules.forbiddenPatterns)('no «%s» in landing config, FAQ and catalog source', (p) => {
    const re = new RegExp(p, 'i')
    all.forEach((s) => expect(s).not.toMatch(re))
  })

  it.each(rules.forbiddenInHero)('no «%s» in the first screen', (p) => {
    heroStrings.forEach((s) => expect(s).not.toMatch(new RegExp(p, 'i')))
  })
})

describe('no sentence is repeated between blocks (CY46-13)', () => {
  it('only the allowed repeats occur twice', () => {
    const blocks: Record<string, string[]> = {
      hero: collectConfigStrings(zapisLanding.hero).text,
      business: collectConfigStrings(zapisLanding.business).text,
      clients: collectConfigStrings(zapisLanding.clients).text,
      faq: zapisFaq.flatMap((f) => [f.question, ...(typeof f.answer === 'string' ? [f.answer] : f.answer), f.link?.label ?? ''].filter(Boolean)),
      catalog: [contract.catalog.title, contract.catalog.subtitle],
    }
    const seen = new Map<string, string>()
    const repeats: string[] = []
    for (const [block, strings] of Object.entries(blocks)) {
      for (const s of new Set(strings)) {
        const prev = seen.get(s)
        if (prev && prev !== block && !rules().includes(s)) repeats.push(`${s} (${prev}, ${block})`)
        seen.set(s, block)
      }
    }
    expect(repeats).toEqual([])
  })
})

function rules(): string[] {
  return contract.rules.allowedRepeats
}
