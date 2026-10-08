import { describe, it, expect } from 'vitest'

// T38-09 (часть шаблона): шаблон не тянет сервисное и не интерпретирует HTML.
const raw = import.meta.glob(['./*.ts', './*.tsx', '!./*.test.ts', '!./*.test.tsx', '!./testConfig.ts'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>
const sources: [string, string][] = Object.entries(raw)

describe('landing template guard (T38-09)', () => {
  it('finds template sources', () => expect(sources.length).toBeGreaterThan(8))
  it.each(sources)('%s: no dangerouslySetInnerHTML', (_f, src) => {
    expect(src).not.toContain('dangerouslySetInnerHTML')
  })
  it.each(sources)('%s: no imports from pages or goods', (_f, src) => {
    expect(src).not.toMatch(/from\s+['"][^'"]*(\/pages\/|\/goods\/|goods\/src)/)
  })
  it('config types expose no className/style/as', () => {
    const types = (raw['./types.ts'] ?? '').replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '')
    expect(types.length).toBeGreaterThan(100)
    expect(types).not.toMatch(/\b(className|style|as)\??:/)
  })
})
