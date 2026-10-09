// Галочка «Получать уведомления…» у клиента и отметка сотрудника (ARCHITECTURE_CYCLE40.md §40.11, §40.15.1).
// Что показывать — решает сервер (`customerMessaging.offered`, подпись `checkboxLabel`); здесь только умолчание из профиля
// (Т40-L-09) и запасные тексты, дословно из LEGAL_REVIEW_CYCLE40.md §5.3, §6.2, §7.3. Слов об обходе блокировок нет.
import type { NotificationPreferences } from '../types'

export type OptInKind = 'booking' | 'order' | 'stay'

/** Ключ правового текста вертикали. */
export const OPT_IN_TEXT_KEY: Record<OptInKind, string> = {
  booking: 'BookingMessengerConsent',
  order: 'OrderMessengerConsent',
  stay: 'StayMessengerConsent',
}

export type OptInDefault =
  | { kind: 'guest'; checked: false }
  | { kind: 'optedOut'; checked: false }
  | { kind: 'prechecked'; checked: true }
  | { kind: 'unchecked'; checked: false }

/**
 * §40.11.4 (Т40-L-09). Гость — снята. Вошедший с `enabled = false` (отписан) — строка вместо галочки, поле не отправляется.
 * `enabled ∧ providerDeliveryConsent` — стоит заранее; иначе (в т.ч. поле не пришло от старого сервера) — снята.
 */
export function resolveOptInDefault(authenticated: boolean, prefs: NotificationPreferences | undefined): OptInDefault {
  if (!authenticated) return { kind: 'guest', checked: false }
  if (!prefs) return { kind: 'unchecked', checked: false }
  if (!prefs.enabled) return { kind: 'optedOut', checked: false }
  if (prefs.providerDeliveryConsent === true) return { kind: 'prechecked', checked: true }
  return { kind: 'unchecked', checked: false }
}

export const OPTED_OUT_TEXT = 'Уведомления в мессенджеры выключены в профиле'
export const OPTED_OUT_LINK = 'Изменить'
export const OPT_IN_MORE = 'Подробнее'
export const OPT_IN_LESS = 'Свернуть'

const NOUN_LOCATIVE: Record<OptInKind, string> = { booking: 'записи', order: 'заказе', stay: 'брони' }

/** Подпись, если сервер при `offered = true` не прислал `checkboxLabel` (по контракту так не бывает). */
export function fallbackCheckboxLabel(kind: OptInKind, messenger: string): string {
  return `Получать уведомления о ${NOUN_LOCATIVE[kind]} в ${messenger}`
}

const SHORT_TAIL: Record<OptInKind, string> = {
  booking: 'Без отметки запись оформляется так же.',
  order: 'Без отметки заказ оформляется так же, а статус виден на странице заказа.',
  stay: 'Без отметки бронь оформляется так же, а все сведения о ней доступны на её странице.',
}


const WHAT_ALLOWED: Record<OptInKind, string> = {
  booking: 'подтверждение, напоминание и сообщения об отмене или переносе этой записи',
  order: 'сообщения о приёме, готовности, изменении и отмене заказа',
  stay: 'подтверждение, напоминание и сообщения об отмене или переносе этой брони',
}

const OBJECT_GEN: Record<OptInKind, string> = { booking: 'записи', order: 'заказа', stay: 'брони' }
const OBJECT_PLURAL_LOC: Record<OptInKind, string> = { booking: 'записях', order: 'заказах', stay: 'бронях' }
const NO_COMPANY_NAME: Record<OptInKind, string> = {
  booking: 'компании, в которую вы записываетесь',
  order: 'магазина, в котором вы оформляете заказ',
  stay: 'компании, у которой вы бронируете',
}

/** Короткая строка (видна всегда) — §5.3 / §6.2. */
export function fallbackShortHtml(kind: OptInKind): string {
  return (
    `<p>Отметка — ваше согласие на сообщения о ${NOUN_LOCATIVE[kind]} в выбранный мессенджер. Для доставки ваше имя, номер телефона и текст сообщения ` +
    'передаются ООО «ГРИН-АПИ» и оператору мессенджера; инфраструктура WhatsApp находится за пределами России, а доступ к нему в России ' +
    `ограничен — сообщение может не дойти. ${SHORT_TAIL[kind]} ` +
    '<a href="/pdn-consent" target="_blank" rel="noopener">Текст согласия</a></p>'
  )
}

/**
 * Полный текст (по «Подробнее») — §5.3, для заказа и брони «как §5.3 с заменой» (§6.2). Сознательно без фразы о письме на
 * {{ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ}}: текст с плейсхолдером не публикуется (ЮРИСТУ в §5.3) — адрес допишет юрист вместе с ключом.
 */
export function fallbackFullHtml(kind: OptInKind): string {
  return (
    '<p><strong>Что вы разрешаете.</strong> Присылать на указанный номер ' +
    `${WHAT_ALLOWED[kind]} от <span data-legal-when="companyName">компании «<span data-legal-value="companyName"></span>»</span>` +
    `<span data-legal-unless="companyName">${NO_COMPANY_NAME[kind]}</span>. Это сервисные сообщения, не реклама.</p>` +
    '<p><strong>Кому передаются данные.</strong> ООО «ГРИН-АПИ» (ИНН 5047259512) — техническая доставка; далее — оператор мессенджера: ' +
    'для WhatsApp — за пределами России, для MAX — ООО «МАХ», данные обрабатываются в России.</p>' +
    '<p><strong>Если вы вошли в аккаунт.</strong> Отметка сохраняется в профиле, в разделе «Согласия», и при следующих ' +
    `${OBJECT_PLURAL_LOC[kind]} будет стоять заранее. Снять её можно для любой ${OBJECT_GEN[kind]}; ` +
    'отозвать согласие совсем — в разделе «Согласия».</p>' +
    '<p><strong>Если вы раньше отказались от сообщений на этот номер,</strong> отказ продолжает действовать и отметкой не отменяется. ' +
    'Включить сообщения снова можно в профиле.</p>' +
    '<p><strong>Отказаться в любой момент</strong> — по ссылке в любом сообщении.</p>'
  )
}

/** Подпись отметки сотрудника (§7.3, продуктовая строка). */
export function staffConsentLabel(messenger: string): string {
  return `Клиент согласился получать сообщения об этой записи в ${messenger}`
}

/** Подсказка под отметкой сотрудника — запасной текст ключа `StaffBookingMessengerConsentHint` (§7.3). */
export function staffHintFallbackHtml(messenger: string): string {
  return (
    '<p>Отмечайте, только если клиент прямо согласился. Скажите ему: «Пришлём подтверждение и напоминание в ' +
    `${messenger}; для этого номер и имя передаются сервису доставки. Отказаться можно по ссылке в сообщении». ` +
    'Отметка сохраняется с вашим именем и временем. Если клиент не ответил или отказался — не отмечайте: запись от этого не меняется.</p>'
  )
}

/** «WhatsApp», «MAX» или «WhatsApp и MAX» из транспортов предложения (для подстановок рядом с текстом). */
export function messengerNames(transports: readonly string[]): string {
  const names = transports.map((t) => (t === 'WhatsApp' ? 'WhatsApp' : t === 'Max' ? 'MAX' : t))
  return names.length === 0 ? 'мессенджере' : names.join(' и ')
}

/**
 * Что отправить в `notifyByMessenger`. Предложено и клиент не отписан → ровно то, что стоит в форме (`true`/`false`).
 * Не предложено или отписан → `undefined` (поле не отправляется; для заказа/брони API требует bool — вызывающий подставляет false).
 */
export function optInPayloadValue(offered: boolean, optedOut: boolean, checked: boolean): boolean | undefined {
  if (!offered || optedOut) return undefined
  return checked
}
