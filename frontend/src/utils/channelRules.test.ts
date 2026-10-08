import { describe, it, expect } from 'vitest'
import vectors from '../../../contracts/cycle40/channel-vectors.json'
import {
  buildMessengerAddons,
  evaluateAvailability,
  evaluateConsent,
  evaluateCustomerOffer,
  evaluateDisplay,
  evaluateFunding,
  evaluateWizardStep,
  pluralDays,
  selectTargets,
  type AddonOptionInput,
  type AvailabilityInput,
  type ConsentInput,
  type DisplayFacts,
  type FundingInput,
  type OfferInput,
  type RoutingCandidate,
  type WizardFacts,
} from './channelRules'

// contracts/cycle40/channel-vectors.json — тот же файл читают юнит-тесты бэкенда (§40.18.6).
const now = new Date(vectors.now)

interface Vector<I, E> {
  name: string
  input: I
  expect: E
}

function table<I, E>(title: string, cases: Vector<I, E>[], run: (input: I) => unknown) {
  describe(title, () => {
    it('таблица векторов не пуста', () => expect(cases.length).toBeGreaterThan(0))
    it.each(cases.map((c) => [c.name, c] as const))('%s', (_n, c) => {
      expect(run(c.input)).toEqual(c.expect)
    })
  })
}

table(
  'availability',
  vectors.availability as unknown as Vector<AvailabilityInput, unknown>[],
  (i) => evaluateAvailability(i),
)
table(
  'optionFunding',
  vectors.optionFunding as unknown as Vector<FundingInput, unknown>[],
  (i) => evaluateFunding(i, now),
)
table(
  'routing',
  vectors.routing as unknown as Vector<
    { mode: 'AllChannels' | 'PriorityChannel'; priority: 'WhatsApp' | 'Max'; candidates: RoutingCandidate[] },
    unknown
  >[],
  (i) => ({ targets: selectTargets(i.mode, i.priority, i.candidates) }),
)
table('consent', vectors.consent as unknown as Vector<ConsentInput, unknown>[], (i) => evaluateConsent(i))
table('display', vectors.display as unknown as Vector<DisplayFacts, unknown>[], (i) => evaluateDisplay(i))
table('wizardStep', vectors.wizardStep as unknown as Vector<WizardFacts, unknown>[], (i) => evaluateWizardStep(i))
table('offer', vectors.offer as unknown as Vector<OfferInput, unknown>[], (i) => evaluateCustomerOffer(i))
table(
  'addons',
  vectors.addons as unknown as Vector<{ options: AddonOptionInput[] }, unknown>[],
  (i) => buildMessengerAddons(i.options),
)

describe('pluralDays', () => {
  it.each([
    [1, '1 день'],
    [2, '2 дня'],
    [5, '5 дней'],
    [11, '11 дней'],
    [14, '14 дней'],
    [21, '21 день'],
  ])('%i -> %s', (n, s) => expect(pluralDays(n)).toBe(s))
})
