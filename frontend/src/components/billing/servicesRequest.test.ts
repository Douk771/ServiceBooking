import { describe, it, expect } from 'vitest'
import { buildPlanRequest, buildOptionsRequest } from './servicesRequest'

const pr = {
  line: 'Services',
  desiredPlanId: 'p1',
  items: [
    { optionId: 'o1', quantity: 2 },
    { optionId: 'o2', quantity: 1, retired: true },
  ],
}

describe('servicesRequest (CY49-F4)', () => {
  it('plan request carries non-retired options of the open Services request', () => {
    expect(buildPlanRequest('p2', pr)).toEqual({ planId: 'p2', options: [{ optionId: 'o1', quantity: 2 }] })
  })
  it('plan request carries nothing without an open request or from another line', () => {
    expect(buildPlanRequest('p2', null)).toEqual({ planId: 'p2', options: [] })
    expect(buildPlanRequest('p2', { ...pr, line: 'Orders' })).toEqual({ planId: 'p2', options: [] })
  })
  it('options request carries the plan of the open Services request', () => {
    expect(buildOptionsRequest({ o3: 1 }, pr)).toEqual({ options: [{ optionId: 'o3', quantity: 1 }], planId: 'p1' })
  })
  it('options request has no planId when the open request has no plan, is absent or of another line', () => {
    expect(buildOptionsRequest({ o3: 1 }, { ...pr, desiredPlanId: null })).not.toHaveProperty('planId')
    expect(buildOptionsRequest({ o3: 1 }, undefined)).not.toHaveProperty('planId')
    expect(buildOptionsRequest({ o3: 1 }, { ...pr, line: 'Baths' })).not.toHaveProperty('planId')
  })
})
