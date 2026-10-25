// @vitest-environment node
import { describe, expect, it } from 'vitest'
import forbidden from '../../../contracts/cycle48/forbidden-phrases.json'
import storefrontPages from '../../../contracts/cycle48/storefront-pages.json'
import zapisLandingJson from '../../../contracts/cycle46/zapis-landing.json'
import { fillOperatorTemplate } from '@/utils/operatorRequisites'
import { zapisLanding } from '@/pages/home/zapisLanding'
import { zapisFaq } from '@/pages/home/zapisFaq'
import { goodsLanding } from '../../goods/src/landing/goodsLanding'
import { goodsFaq } from '../../goods/src/landing/goodsFaq'
import { stayLanding } from '../../dom/src/landing/stayLanding'
import { stayFaq } from '../../dom/src/landing/stayFaq'
import { baniLanding } from '../../bani/src/landing/baniLanding'
import { baniFaq } from '../../bani/src/landing/baniFaq'

// CY48-07 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.13.1, SPEC US-48-04): nothing a guest reads on a public page contains the
// phrases of contracts/cycle48/forbidden-phrases.json (scope `publicCopy`). The same list is checked on the C# side for tariff texts.
interface Pattern {
  id: string
  regex: string
  ignoreCase: boolean
  scopes: string[]
}
const patterns = (forbidden.patterns as Pattern[]).filter((p) => p.scopes.includes('publicCopy'))
const regexes = patterns.map((p) => ({ id: p.id, re: new RegExp(p.regex, p.ignoreCase ? 'i' : '') }))

const SKIP_KEYS = (k: string) => k.startsWith('_') || k.startsWith('legacy') || ['markup', 'source', 'placeholders', 'sites', 'type', 'id', 'path', 'contract', 'version', 'changes', 'consumers', 'rules'].includes(k)

/** Every string of a value, templates filled with the operator values (what the visitor really sees). */
function strings(v: unknown, key = ''): string[] {
  if (SKIP_KEYS(key)) return []
  if (typeof v === 'string') return [v.includes('{{') ? fillOperatorTemplate(v) : v]
  if (Array.isArray(v)) return v.flatMap((x) => strings(x, key))
  if (v !== null && typeof v === 'object') return Object.entries(v).flatMap(([k, x]) => strings(x, k))
  return []
}

const sources: Record<string, unknown> = {
  zapisLanding,
  zapisFaq,
  goodsLanding,
  goodsFaq,
  stayLanding,
  stayFaq,
  baniLanding,
  baniFaq,
  'storefront-pages.json': storefrontPages,
  'zapis-landing.json': zapisLandingJson,
}

describe('CY48-07 forbidden phrases in public copy', () => {
  it('the scope has patterns', () => {
    expect(regexes.map((r) => r.id)).toEqual(expect.arrayContaining(['after-request', 'we-will-contact', 'depends-on-count', 'by-count', 'leftover-placeholder']))
  })

  it.each(Object.keys(sources))('%s has no forbidden phrase', (name) => {
    const all = strings(sources[name])
    expect(all.length).toBeGreaterThan(3)
    const hits = all.flatMap((s) => regexes.filter((r) => r.re.test(s)).map((r) => `${r.id}: ${s}`))
    expect(hits).toEqual([])
  })

  it('the patterns do catch the old texts and let the new ones pass (the guard is not blind)', () => {
    const hit = (s: string) => regexes.some((r) => r.re.test(s))
    expect(hit('Итоговая цена — после заявки, по числу филиалов')).toBe(true)
    expect(hit('Чтобы сменить тариф, оставьте заявку в кабинете, и мы свяжемся с вами.')).toBe(true)
    expect(hit('Стоимость от 3 900 ₽ в месяц, зависит от числа филиалов.')).toBe(true)
    expect(hit('Цена не зависит от числа филиалов')).toBe(false)
    expect(hit('Цена не зависит от числа гостей')).toBe(false)
    expect(hit('{{СРОК_СЧЁТА}}')).toBe(true)
  })
})
