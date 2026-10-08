import type { ReminderDropReason, ReminderErrorCode, ReminderWarning } from '../types'

/**
 * The editor of the arrival-reminder template (ARCHITECTURE_CYCLE39.md §39.11). The render, the filter and the checks are the
 * SERVER's (`ArrivalReminderTemplate`, preview route); this file only holds the words for the codes it sends, the half-hour choices
 * of the sending time and the insertion of a placeholder at the cursor.
 */

/** «Время — с 08:00 до 22:00 с шагом 30 минут» (§39.33.5). */
export const REMINDER_TIME_OPTIONS: string[] = Array.from({ length: 29 }, (_, i) => {
  const minutes = 8 * 60 + i * 30
  return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${minutes % 60 === 0 ? '00' : '30'}`
})

/** Soft warnings (`warnings[]`, not a refusal) — the words of §39.33.5. */
export const REMINDER_WARNING_TEXT: Record<ReminderWarning, string> = {
  CancellationTermsInText: 'Условия отмены задаёт выбранный шаблон — другие условия в тексте напоминания не действуют',
  Passport: 'Не просите гостя прислать фото паспорта: документ при заселении проверяете вы на месте',
  CardNumber: 'Похоже на номер карты — не отправляйте данные карт в сообщениях',
  PassportNumber: 'Похоже на паспортные данные — не отправляйте их в сообщениях',
}

/** Why a line of the owner's text does not reach the push (ЮР39-3). */
export const REMINDER_DROP_TEXT: Record<ReminderDropReason, string> = {
  ForbiddenPlaceholder: 'подстановка с личными данными или ссылкой не показывается в push',
  EmptyValue: 'значение подстановки пустое',
  Digits: '4 и больше цифр подряд',
  Link: 'ссылка',
  Email: 'адрес электронной почты',
  Phone: 'номер телефона',
  CodeWord: 'слова про код, пароль или Wi-Fi',
}

export const REMINDER_ERROR_TEXT: Record<ReminderErrorCode, string> = {
  TooLong: 'Текст напоминания — не длиннее 700 символов',
  UnknownPlaceholder: 'В тексте есть неизвестная подстановка',
  ForbiddenWords: 'Не используйте слова «задаток», «невозвратный», «депозит»',
}

export const REMINDER_PUSH_ACK_TEXT = 'Я прочитал предупреждение о push'

/** Inserts `token` over the selection; returns the new text and where the cursor goes (right after the token). */
export function insertAtCursor(text: string, selectionStart: number, selectionEnd: number, token: string): { text: string; cursor: number } {
  const start = Math.max(0, Math.min(selectionStart, text.length))
  const end = Math.max(start, Math.min(selectionEnd, text.length))
  return { text: text.slice(0, start) + token + text.slice(end), cursor: start + token.length }
}

/** The template that goes into the request: null means «по умолчанию» (a text equal to the default is stored as null). */
export function templateForSave(draft: string, defaultTemplate: string): string | null {
  const normalized = draft.replace(/\r\n/g, '\n')
  return normalized.trim() === '' || normalized === defaultTemplate ? null : normalized
}

/** A draft equal to the default (or empty) is «по умолчанию»: the button «Вернуть по умолчанию» is then pointless. */
export function isDefaultDraft(draft: string, defaultTemplate: string): boolean {
  return templateForSave(draft, defaultTemplate) === null
}
