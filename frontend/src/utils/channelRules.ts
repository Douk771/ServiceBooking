/**
 * Клиентская проекция правил каналов рассылок цикла 40 (ARCHITECTURE_CYCLE40.md §40.3.1, §40.5, §40.6, §40.7.1, §40.10,
 * §40.11.2, §40.14). Источник истины — сервер; эталон — contracts/cycle40/channel-vectors.json, который прогоняет
 * channelRules.test.ts.
 *
 * ВАЖНО (§40.18.6): UI НЕ вычисляет то, что приходит полями сервера (`open`, `action`, `wizardStep`, `canRequestPayment`,
 * `displayText`, `deliveryChoiceVisible`, `customerMessaging.offered`, `providerDeliveryConsent`, `messengerAddons`).
 * Эти функции нужны для моков/фикстур, превью и сверки с эталоном, а не для ветвления интерфейса в обход сервера.
 */
import type { components } from '../types/api-cycle40.generated'

type Schemas = components['schemas']

export type Transport = Schemas['NotificationTransport']
export type ChannelState = Schemas['ChannelState']
export type DisplayStatus = Schemas['ChannelDisplayStatus']
export type ChannelAction = Schemas['ChannelAction']
export type WizardStep = Schemas['ChannelWizardStep']

/** Порядок транспортов везде один: WhatsApp, затем MAX. */
const TRANSPORT_ORDER: Transport[] = ['WhatsApp', 'Max']

export function transportLabel(t: Transport): string {
  return t === 'Max' ? 'MAX' : 'WhatsApp'
}

function byTransportOrder<T extends { transport: Transport }>(items: T[]): T[] {
  return [...items].sort((a, b) => TRANSPORT_ORDER.indexOf(a.transport) - TRANSPORT_ORDER.indexOf(b.transport))
}

// ---------- §40.7.1 доступность ----------

export interface AvailabilityInput {
  transport: Transport
  settingValue: string | null
  configDefault: boolean
  isActive: boolean
  pricePerMonth: number | null
  termsOwnerPublished: boolean
}

export function evaluateAvailability(i: AvailabilityInput): { open: boolean; sellable: boolean } {
  const open = i.settingValue === 'true' ? true : i.settingValue === 'false' ? false : i.configDefault
  const sellable = open && i.isActive && i.pricePerMonth !== null && i.termsOwnerPublished
  return { open, sellable }
}

// ---------- §40.3.1 оплата транспорта ----------

export interface FundingInput {
  row: {
    exists: boolean
    endsAtUtc: string | null
    paidUntilUtc: string | null
    grantedByTrial: boolean
    activatedAtUtc: string | null
  }
  trialEndsAtUtc: string | null
  subscription: { usable: boolean; paidUntil: string | null }
}

export function evaluateFunding(i: FundingInput, now: Date): { paid: boolean; paidUntil: string | null; isTrial: boolean } {
  const { row } = i
  if (!row.exists) return { paid: false, paidUntil: null, isTrial: false }
  const isTrial = row.grantedByTrial
  const t = now.getTime()
  if (row.endsAtUtc !== null && new Date(row.endsAtUtc).getTime() <= t) {
    return { paid: false, paidUntil: row.endsAtUtc, isTrial }
  }
  if (row.paidUntilUtc !== null) {
    return { paid: new Date(row.paidUntilUtc).getTime() >= t, paidUntil: row.paidUntilUtc, isTrial }
  }
  if (row.grantedByTrial) {
    return {
      paid: i.trialEndsAtUtc !== null && new Date(i.trialEndsAtUtc).getTime() > t,
      paidUntil: i.trialEndsAtUtc,
      isTrial,
    }
  }
  return { paid: i.subscription.usable, paidUntil: i.subscription.paidUntil, isTrial }
}

// ---------- §40.5 маршрутизация ----------

export type DeliveryMode = 'AllChannels' | 'PriorityChannel'

export interface RoutingCandidate {
  transport: Transport
  paid: boolean
  funded: boolean
  suspended: boolean
  state: ChannelState
}

const ROUTABLE_STATES: ChannelState[] = ['Connected', 'Disconnected', 'NeedsReconnect', 'Blocked']

export function isRoutable(c: RoutingCandidate): boolean {
  return c.paid && c.funded && !c.suspended && ROUTABLE_STATES.includes(c.state)
}

export function selectTargets(mode: DeliveryMode, priority: Transport, candidates: RoutingCandidate[]): Transport[] {
  const routable = byTransportOrder(candidates.filter(isRoutable))
  if (routable.length === 0) return []
  if (routable.length === 1) return [routable[0].transport]
  if (mode === 'AllChannels') return routable.map((c) => c.transport)
  return [priority]
}

// ---------- §40.11.2 согласие ----------

export type ConsentSource = 'Guest' | 'Customer' | 'Staff'

export interface ConsentInput {
  source: ConsentSource
  notifyByMessenger: boolean | null
  recipientIsAccount: boolean
  accountHasProviderDeliveryConsent: boolean
  optedOut: boolean
}

export interface ConsentResult {
  decision: 'Allowed' | 'Declined' | 'NoConsent' | null
  skipReason: 'ClientDeclinedMessenger' | 'NoProviderDeliveryConsent' | 'RecipientOptedOut' | null
  stored: { notifyByMessenger: boolean | null; consentByStaff: boolean }
  writesConsentLedger: boolean
}

export function evaluateConsent(i: ConsentInput): ConsentResult {
  // Сотрудник может только дать согласие: «нет» у него хранится как «не отмечено».
  const stored = {
    notifyByMessenger: i.source === 'Staff' && i.notifyByMessenger === false ? null : i.notifyByMessenger,
    consentByStaff: i.source === 'Staff' && i.notifyByMessenger === true,
  }
  const writesConsentLedger = i.source === 'Customer' && i.notifyByMessenger === true && !i.accountHasProviderDeliveryConsent
  if (i.optedOut) return { decision: null, skipReason: 'RecipientOptedOut', stored, writesConsentLedger }
  if (stored.notifyByMessenger === false) {
    return { decision: 'Declined', skipReason: 'ClientDeclinedMessenger', stored, writesConsentLedger }
  }
  if (stored.notifyByMessenger === true) return { decision: 'Allowed', skipReason: null, stored, writesConsentLedger }
  const allowed = i.recipientIsAccount && i.accountHasProviderDeliveryConsent
  return allowed
    ? { decision: 'Allowed', skipReason: null, stored, writesConsentLedger }
    : { decision: 'NoConsent', skipReason: 'NoProviderDeliveryConsent', stored, writesConsentLedger }
}

// ---------- §40.6 отображение и шаг мастера ----------

export interface DisplayFacts {
  transport: Transport
  state: ChannelState
  lastStateReason: string | null
  phoneMasked: string | null
  paid: boolean
  paidUntil: string | null
  isTrial: boolean
  requestNewerThanPayment: boolean
  isDuplicate: boolean
  suspended: boolean
  platformEnabled: boolean
  optionOpen: boolean
  optionSellable: boolean
  termsAccepted: boolean
  idleSinceUtc: string | null
  idleDeadlineUtc: string | null
  idleDays: number | null
}

export interface DisplayResult {
  displayStatus: DisplayStatus | null
  displayText: string | null
  action: ChannelAction | null
}

/** 1 день, 2 дня, 5 дней, 21 день. */
export function pluralDays(n: number): string {
  const m100 = n % 100
  const m10 = n % 10
  if (m100 >= 11 && m100 <= 14) return `${n} дней`
  if (m10 === 1) return `${n} день`
  if (m10 >= 2 && m10 <= 4) return `${n} дня`
  return `${n} дней`
}

// Даты серверные, в UTC: берём календарную дату из ISO-строки, а не из локального пояса браузера.
function formatDate(iso: string, withYear: boolean): string {
  const [y, m, d] = iso.slice(0, 10).split('-')
  return withYear ? `${d}.${m}.${y}` : `${d}.${m}`
}

const PRE_CONNECT_STATES: ChannelState[] = ['NotConnected', 'NeedsReconnect', 'Disconnected']

export function evaluateDisplay(f: DisplayFacts): DisplayResult {
  const M = transportLabel(f.transport)
  const mismatch = f.lastStateReason === 'ServerCountryMismatch'
  const r = (displayStatus: DisplayStatus, displayText: string, action: ChannelAction | null = null): DisplayResult => ({
    displayStatus,
    displayText,
    action,
  })
  if (f.state === 'Replaced') return { displayStatus: null, displayText: null, action: null }
  if (!f.platformEnabled) return r('Off', 'Рассылки временно отключены платформой')
  if (f.suspended) return r('Off', 'Приостановлен администратором')
  if (f.state === 'DisabledByOwner') {
    return r('Off', 'Отключён вами', f.paid ? 'BindNumber' : f.optionSellable ? 'Pay' : null)
  }
  if (f.isDuplicate) {
    return r('ActionRequired', `Лишний номер ${M}: сообщения уходят с другого номера. Отвяжите этот`, 'Unbind')
  }
  if (!f.paid && f.requestNewerThanPayment) return r('ActionRequired', 'Оплата на проверке')
  if (!f.paid && !f.optionSellable) return r('ActionRequired', `Подключение ${M} временно недоступно`)
  if (!f.paid && f.paidUntil !== null) {
    return r('ActionRequired', `Оплата закончилась ${formatDate(f.paidUntil, true)}`, 'Pay')
  }
  if (!f.paid) return r('ActionRequired', 'Не оплачено', 'Pay')
  if (PRE_CONNECT_STATES.includes(f.state) && !(f.state === 'Disconnected' && mismatch) && !f.termsAccepted) {
    return r('ActionRequired', `Примите условия подключения ${M}`, 'AcceptTerms')
  }
  if (f.state === 'NotConnected') return r('ActionRequired', 'Оплата подтверждена — привяжите номер', 'BindNumber')
  if (f.state === 'Connecting') return r('ActionRequired', 'Номер привязывается — отсканируйте QR', 'BindNumber')
  if (f.state === 'Disconnected' && mismatch) return r('ActionRequired', 'Требуется вмешательство платформы')
  if (f.state === 'Disconnected') {
    return r('ActionRequired', `Связь с ${M} разорвана — подключите номер заново`, 'Reconnect')
  }
  if (f.state === 'NeedsReconnect' && f.lastStateReason === 'SecretUnavailable') {
    return r('ActionRequired', 'Нужна повторная привязка после технических работ. Оплата сохранена', 'Reconnect')
  }
  if (f.state === 'NeedsReconnect') {
    const days = f.idleDays !== null ? pluralDays(f.idleDays) : ''
    return r(
      'ActionRequired',
      `Номер отключён: им ${days} никто не пользовался. Оплата сохранена — подключите заново`,
      'Reconnect',
    )
  }
  if (f.state === 'Blocked') {
    return r('ActionRequired', `${M} заблокировал этот номер. Подключите другой — оплата сохранится`, 'ReplaceNumber')
  }
  if (f.state === 'Connected' && f.idleSinceUtc !== null) {
    const deadline = f.idleDeadlineUtc !== null ? formatDate(f.idleDeadlineUtc, false) : ''
    return r('ActionRequired', `Номер отключится ${deadline}: ни у одной вашей компании не включены сообщения клиентам`)
  }
  if (f.state === 'Connected') return r('Working', `Сообщения уходят с номера ${f.phoneMasked ?? ''}`.trimEnd())
  return { displayStatus: null, displayText: null, action: null }
}

export interface WizardFacts extends DisplayFacts {
  hasChannel: boolean
}

const QR_STATES: ChannelState[] = ['NotConnected', 'Connecting', 'Disconnected', 'NeedsReconnect', 'DisabledByOwner']

export function evaluateWizardStep(f: WizardFacts): { wizardStep: WizardStep; canRequestPayment: boolean } {
  const canRequestPayment = (!f.paid || f.isTrial) && f.optionSellable
  const mismatch = f.lastStateReason === 'ServerCountryMismatch'
  const step = (wizardStep: WizardStep) => ({ wizardStep, canRequestPayment })
  if (!f.platformEnabled || (!f.paid && !f.optionSellable)) return step('Unavailable')
  if (!f.paid && f.requestNewerThanPayment) return step('PaymentPending')
  if (!f.paid) return step('Payment')
  if (f.suspended || f.state === 'Blocked' || (f.state === 'Disconnected' && mismatch) || f.isDuplicate) return step('None')
  // При hasChannel=false поле state в фактах — заглушка (векторы держат там Connected): «нет номера» -> Terms, не Done.
  if (f.hasChannel && f.state === 'Connected') return step('Done')
  if (!f.hasChannel || !f.termsAccepted) return step('Terms')
  if (QR_STATES.includes(f.state)) return step('Qr')
  return step('None')
}

// ---------- §40.10 «Рассылки работают» ----------

export type OfferKind = 'Services' | 'Orders' | 'Stays'

export interface OfferInput {
  platformEnabled: boolean
  kind: OfferKind
  companyFlag: boolean
  showcase: boolean
  companyActive: boolean
  mode: DeliveryMode
  priority: Transport
  transports: Partial<Record<Transport, { routable: boolean; working: boolean }>>
}

const OFFER_NOUN: Record<OfferKind, string> = { Services: 'записи', Orders: 'заказе', Stays: 'брони' }

export function evaluateCustomerOffer(i: OfferInput): { offered: boolean; transports: Transport[]; checkboxLabel: string | null } {
  const none = { offered: false, transports: [] as Transport[], checkboxLabel: null }
  if (!i.platformEnabled || !i.companyActive || i.showcase || !i.companyFlag) return none
  const routable: RoutingCandidate[] = TRANSPORT_ORDER.filter((t) => i.transports[t]?.routable).map((transport) => ({
    transport,
    paid: true,
    funded: true,
    suspended: false,
    state: 'Connected',
  }))
  const targets = selectTargets(i.mode, i.priority, routable).filter((t) => i.transports[t]?.working)
  if (targets.length === 0) return none
  const names = byTransportOrder(targets.map((transport) => ({ transport })))
    .map((x) => transportLabel(x.transport))
    .join(' и ')
  return {
    offered: true,
    transports: byTransportOrder(targets.map((transport) => ({ transport }))).map((x) => x.transport),
    checkboxLabel: `Получать уведомления о ${OFFER_NOUN[i.kind]} в ${names}`,
  }
}

// ---------- §40.14 цены мессенджеров ----------

export interface AddonOptionInput {
  code: string
  open: boolean
  isActive: boolean
  pricePerMonth: number | null
  legallySellable: boolean
}

export interface MessengerAddon {
  transport: Transport
  label: string
  pricePerMonth: number
  text: string
  footnote: string | null
}

export const WHATSAPP_FOOTNOTE = 'Доступ к WhatsApp в России ограничен: сообщения могут не доходить'

const OPTION_CODE_TRANSPORT: Record<string, Transport> = {
  'notifications.whatsapp': 'WhatsApp',
  'notifications.max': 'Max',
}

/** 490 -> «490», 490.5 -> «490,50». */
export function formatAddonPrice(price: number): string {
  return Number.isInteger(price) ? String(price) : price.toFixed(2).replace('.', ',')
}

export function buildMessengerAddons(options: AddonOptionInput[]): { messengerAddons: MessengerAddon[]; noteShown: boolean } {
  const lines = options.flatMap((o): MessengerAddon[] => {
    const transport = OPTION_CODE_TRANSPORT[o.code]
    if (!transport || !o.open || !o.isActive || o.pricePerMonth === null || !o.legallySellable) return []
    const label = transportLabel(transport)
    return [
      {
        transport,
        label,
        pricePerMonth: o.pricePerMonth,
        text: `+ ${label} ${formatAddonPrice(o.pricePerMonth)} ₽/мес`,
        footnote: transport === 'WhatsApp' ? WHATSAPP_FOOTNOTE : null,
      },
    ]
  })
  const messengerAddons = byTransportOrder(lines)
  return { messengerAddons, noteShown: messengerAddons.length > 0 }
}
