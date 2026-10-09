import { describe, it, expect } from 'vitest'
import { evaluateWizardStep } from '../../utils/channelRules'
import { closedWhatsAppWithNumber, maxTrialOnTermsStep, overviewMaxTrialWhatsAppHidden } from './numbersOverview'

// Страж согласованности фикстур с правилами: фикстура не должна противоречить эталону.
describe('numbersOverview fixtures', () => {
  it('триал на шаге Terms: оплачено, условия не приняты, заявка на оплату возможна', () => {
    const t = maxTrialOnTermsStep()
    const r = evaluateWizardStep({
      transport: t.transport,
      state: 'NotConnected',
      lastStateReason: null,
      phoneMasked: null,
      paid: t.paid,
      paidUntil: t.paidUntil ?? null,
      isTrial: t.isTrial,
      requestNewerThanPayment: false,
      isDuplicate: false,
      suspended: false,
      platformEnabled: true,
      optionOpen: t.open,
      optionSellable: t.sellable,
      termsAccepted: t.termsAccepted,
      idleSinceUtc: null,
      idleDeadlineUtc: null,
      idleDays: null,
      hasChannel: false,
    })
    expect(r).toEqual({ wizardStep: t.wizardStep, canRequestPayment: t.canRequestPayment })
  })

  it('закрытый WhatsApp с номером: open=false, оплатить нельзя', () => {
    const t = closedWhatsAppWithNumber()
    expect(t.open).toBe(false)
    expect(t.sellable).toBe(false)
    expect(t.canRequestPayment).toBe(false)
  })

  it('overview без закрытого WhatsApp содержит только MAX', () => {
    expect(overviewMaxTrialWhatsAppHidden().transports.map((x) => x.transport)).toEqual(['Max'])
  })
})
