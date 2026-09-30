// @vitest-environment node
// CY36-02 (ARCHITECTURE_CYCLE36.md §36.10): every vitest file belongs to at least one area of
// contracts/cycle36/test-areas.json, so `npm run test:area` never silently skips a file.
import { describe, it, expect } from 'vitest'
import testAreas from '../../../contracts/cycle36/test-areas.json'

const modules = import.meta.glob(['../**/*.test.{ts,tsx}', '../../goods/src/**/*.test.{ts,tsx}'])

// Keys are relative to this file (`./x.test.ts`, `../pages/X.test.tsx`, `../../goods/src/…`); resolve them against the frontend root.
const frontendRoot = new URL('../../', import.meta.url).pathname
function toRepoPath(key: string): string {
  return new URL(key, import.meta.url).pathname.slice(frontendRoot.length)
}

describe('CY36-02 test areas cover every vitest file', () => {
  it('CY36-02 every *.test.ts(x) matches a frontendPathPrefixes entry of some area', () => {
    const prefixes = testAreas.areas.flatMap((a) => a.frontendPathPrefixes)
    const files = Object.keys(modules).map(toRepoPath)
    expect(files.length).toBeGreaterThan(100)
    const orphans = files.filter((f) => !prefixes.some((p) => f.startsWith(p)))
    expect(orphans, `files outside every area — add a prefix to contracts/cycle36/test-areas.json:\n${orphans.join('\n')}`).toEqual([])
  })

  it('CY36-02 area ids are unique and the list is non-empty', () => {
    const ids = testAreas.areas.map((a) => a.id)
    expect(ids.length).toBeGreaterThan(0)
    expect(new Set(ids).size).toBe(ids.length)
  })
})
