/** Общие классы секций шаблона — одно место для отступов и заголовков (ARCHITECTURE_CYCLE38.md §38.3.2). */
/** Отступ до линии и после неё одинаковый: 32 px на телефоне, 48 px на компьютере. */
export const SECTION = 'mt-8 md:mt-12 border-t border-line pt-8 md:pt-12'
/** Отдельная секция с панелью шагов («Запись», блок 4): разделитель — сама панель. */
export const STEPS_SECTION = 'mt-8 md:mt-12'
export const EYEBROW = 'text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark'
export const H2 = 'font-serif text-[32px] sm:text-[40px] leading-[1.1] font-medium text-ink'
export const H3 = 'font-serif text-[26px] sm:text-[28px] leading-[1.2] font-medium text-ink'
export const LEAD = 'mt-5 text-[16px] leading-relaxed text-ink-soft max-w-[480px]'
export const BTN_PRIMARY =
  'inline-flex items-center gap-2 bg-ink hover:bg-ink/90 !text-cream text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors'
export const BTN_SECONDARY =
  'inline-flex items-center bg-white hover:bg-cream-deep !text-ink border border-line text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors'
export const CHECK_ITEM = 'flex items-start gap-3 text-[15px] leading-[1.6] text-ink'
export const FOCUS_RING =
  'focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-gold-dark'
/** Контейнер секций в ветке шапки с панелью (ARCHITECTURE_CYCLE41.md §41.3.2). */
export const CONTAINER = 'max-w-[1180px] mx-auto px-4 sm:px-8'

/** Ряд действий: пара кнопок в одну строку на 375 px; flex-wrap — страховка для экранов уже 360 px. */
export const ACTIONS_ROW = 'mt-6 flex flex-wrap items-center gap-2 sm:gap-3'
export const BTN_ACTION_PRIMARY =
  'inline-flex items-center justify-center gap-2 min-h-[44px] whitespace-nowrap rounded-full bg-ink hover:bg-ink/90 !text-cream text-[14px] sm:text-[15px] font-semibold px-4 sm:px-7 py-2.5 sm:py-3.5 transition-colors'
export const BTN_ACTION_SECONDARY =
  'inline-flex items-center justify-center min-h-[44px] whitespace-nowrap rounded-full bg-white hover:bg-cream-deep !text-ink border border-line text-[14px] sm:text-[15px] font-semibold px-4 sm:px-7 py-2.5 sm:py-3.5 transition-colors'
/** Плотный список с галочкой; колонки задаёт компонент. */
export const DENSE_LIST = 'grid gap-x-8 gap-y-2.5'
/** Кружок иконки шага в одном ряду с заголовком. */
export const STEP_ICON = 'w-9 h-9 shrink-0 rounded-full bg-cream border border-line-strong flex items-center justify-center'
