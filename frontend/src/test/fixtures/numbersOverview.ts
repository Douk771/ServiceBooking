import type { NumbersOverviewDto, TransportNumbersDto } from '../../api/notificationNumbers'

// Фикстуры ответа GET /api/notification-channels/overview по contracts/cycle40/openapi.yaml (x-examples).
// Только для тестов и моков: значения полей — «как отдал бы сервер», фронт их не вычисляет (§40.18.6).

const QR_INSTRUCTION_MAX = [
  'Отключите пароль входа в настройках MAX',
  'Откройте MAX на телефоне с номером, который будет отправлять сообщения',
  'Профиль → Устройства → Подключить устройство',
  'Наведите камеру на QR-код на этом экране',
]

const QR_INSTRUCTION_WHATSAPP = [
  'Откройте WhatsApp на телефоне с номером, который будет отправлять сообщения',
  'Настройки → Связанные устройства → Привязка устройства',
  'Наведите камеру на QR-код на этом экране',
]

/** MAX: открыт, продаётся. Остальные поля переопределяются в тесте. */
export function maxTransport(overrides: Partial<TransportNumbersDto> = {}): TransportNumbersDto {
  return {
    transport: 'Max',
    displayName: 'MAX',
    open: true,
    pricePerMonth: 490,
    priceText: '490 ₽/мес',
    sellable: true,
    unavailableText: null,
    paid: false,
    paidUntil: null,
    isTrial: false,
    paymentPending: false,
    canRequestPayment: true,
    termsAccepted: false,
    wizardStep: 'Payment',
    // A transport WITHOUT a number carries no row-level status or action in the server's answer (contract example, §40.23): the entry into the wizard is wizardStep.
    displayStatus: null,
    displayText: null,
    action: null,
    channel: null,
    extraChannels: [],
    connectionNotice: 'Для авторизации по QR в MAX нужно отключить пароль входа в мессенджере.',
    qrInstruction: QR_INSTRUCTION_MAX,
    prefill: null,
    ...overrides,
  }
}

/** Закрытый WhatsApp (Р40-Ю1): у аккаунта нет номера и оплаты — сервер такой транспорт в overview не присылает вовсе. */
export const CLOSED_WHATSAPP_ABSENT = undefined

/**
 * Закрытый WhatsApp у аккаунта, у которого уже есть номер: показывается с `open: false`, управление номером
 * доступно, оплатить/продлить нельзя (`canRequestPayment: false`, `sellable: false`).
 */
export function closedWhatsAppWithNumber(overrides: Partial<TransportNumbersDto> = {}): TransportNumbersDto {
  return {
    transport: 'WhatsApp',
    displayName: 'WhatsApp',
    open: false,
    pricePerMonth: null,
    priceText: null,
    sellable: false,
    unavailableText: 'Подключение WhatsApp сейчас недоступно',
    paid: true,
    paidUntil: '2026-11-20T00:00:00Z',
    isTrial: false,
    paymentPending: false,
    canRequestPayment: false,
    termsAccepted: true,
    wizardStep: 'Done',
    displayStatus: 'Working',
    displayText: 'Сообщения уходят с номера +7 *** ***-45-67',
    action: null,
    channel: null,
    extraChannels: [],
    connectionNotice: null,
    qrInstruction: QR_INSTRUCTION_WHATSAPP,
    prefill: null,
    ...overrides,
  }
}

/** MAX по триалу: оплачено, но условия не приняты — мастер стартует с шага `Terms` (Т40-L-03), без заявки на оплату. */
export function maxTrialOnTermsStep(overrides: Partial<TransportNumbersDto> = {}): TransportNumbersDto {
  return maxTransport({
    paid: true,
    paidUntil: '2026-10-23T00:00:00Z',
    isTrial: true,
    canRequestPayment: true,
    termsAccepted: false,
    wizardStep: 'Terms',
    displayStatus: null,
    displayText: null,
    action: null,
    ...overrides,
  })
}

export function numbersOverview(
  transports: TransportNumbersDto[],
  overrides: Partial<NumbersOverviewDto> = {},
): NumbersOverviewDto {
  return {
    messagingEnabled: true,
    messagingDisabledText: null,
    note: 'Номера общие для всех ваших компаний',
    companies: [{ id: '0a1b2c3d-1111-4222-8333-444455556666', name: 'Салон «Лён»', kind: 'Services', isActive: true }],
    companiesText: 'Работает для всех ваших компаний: 1',
    offer: { version: '2026-10-20', url: '/offer-channel' },
    risk: { version: '2026-10-20', html: '<p>Текст о рисках</p>', url: '/channel-risk' },
    statusNotice: 'Мессенджеры подключаются только тем, кто указал статус ИП, организации или самозанятого',
    transports,
    ...overrides,
  }
}

/** Закрытый WhatsApp не показывается, MAX — триал на шаге «Условия» (x-example MaxOnTermsStepWhatsAppHidden). */
export const overviewMaxTrialWhatsAppHidden = (): NumbersOverviewDto => numbersOverview([maxTrialOnTermsStep()])

/** У аккаунта есть номер WhatsApp, но опция закрыта; MAX свободен для покупки. */
export const overviewWhatsAppClosedWithNumber = (): NumbersOverviewDto =>
  numbersOverview([closedWhatsAppWithNumber(), maxTransport()])
