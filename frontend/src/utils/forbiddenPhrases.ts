// ARCHITECTURE_CYCLE48_STOREFRONT.md §48.13.1 (CY48-07) — checks strings against contracts/cycle48/forbidden-phrases.json, scope `publicCopy`.
// Pure helper for the guard tests of the four frontends (src/test/forbiddenPhrases.guard.test.ts and the ones next to the landing configs
// of goods, dom and bani); no application code imports it.
import forbidden from '../../../contracts/cycle48/forbidden-phrases.json'
import { fillOperatorTemplate } from '@/utils/operatorRequisites'

interface Pattern {
  id: string
  regex: string
  ignoreCase: boolean
  scopes: string[]
}

export function forbiddenRegexes(scope: 'publicCopy' | 'tariffTexts'): Array<{ id: string; re: RegExp }> {
  return (forbidden.patterns as Pattern[])
    .filter((p) => p.scopes.includes(scope))
    .map((p) => ({ id: p.id, re: new RegExp(p.regex, p.ignoreCase ? 'i' : '') }))
}

const SKIP_KEYS = new Set([
  'markup',
  'source',
  'placeholders',
  'sites',
  'type',
  'id',
  'path',
  'contract',
  'version',
  'changes',
  'consumers',
  'rules',
])
const skip = (k: string) => k.startsWith('_') || k.startsWith('legacy') || SKIP_KEYS.has(k)

/** Every string of a config/JSON value, `{{KEY}}` filled with the operator values (what the visitor really reads). */
export function visibleStrings(v: unknown, key = ''): string[] {
  if (skip(key)) return []
  if (typeof v === 'string') return [v.includes('{{') ? fillOperatorTemplate(v) : v]
  if (Array.isArray(v)) return v.flatMap((x) => visibleStrings(x, key))
  if (v !== null && typeof v === 'object') return Object.entries(v).flatMap(([k, x]) => visibleStrings(x, k))
  return []
}

/** `<pattern id>: <string>` for every string that matches a `publicCopy` pattern. */
export function publicCopyViolations(value: unknown): string[] {
  const regexes = forbiddenRegexes('publicCopy')
  return visibleStrings(value).flatMap((s) => regexes.filter((r) => r.re.test(s)).map((r) => `${r.id}: ${s}`))
}
