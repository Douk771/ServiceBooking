import { describe, it, expect } from 'vitest'
import { STATUS_BADGE_CLASS } from './billingPageHelpers'

// N23: a Free/Expired subscription must not show the same green "success" badge as Active — the
// bug was a hardcoded className that ignored `data.status` entirely.
describe('STATUS_BADGE_CLASS', () => {
  it('gives Active a distinct success style', () => {
    expect(STATUS_BADGE_CLASS.Active).toContain('success')
  })

  it('gives Expired a danger style, not success', () => {
    expect(STATUS_BADGE_CLASS.Expired).toContain('danger')
    expect(STATUS_BADGE_CLASS.Expired).not.toContain('success')
  })

  it('gives Free a neutral style, not success', () => {
    expect(STATUS_BADGE_CLASS.Free).not.toContain('success')
    expect(STATUS_BADGE_CLASS.Free).not.toContain('danger')
  })
})
