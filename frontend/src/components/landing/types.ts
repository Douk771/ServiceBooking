import type { IconName } from '../ui/Icon'
import type { PricingLine } from '../pricing/pricingLine'

/**
 * Типы конфига единого шаблона главной (ARCHITECTURE_CYCLE38.md §38.3.1).
 * Полей className/style/as нет намеренно: перекрасить секцию из конфига нельзя.
 */
export type AnchorHref = `#${string}`
export interface AnchorAction {
  kind: 'anchor'
  href: AnchorHref
  label: string
}
export interface RouteAction {
  kind: 'route'
  to: string
  label: string
}
/** Адрес зависит от входа: useAuthStore(s => s.isAuthenticated()). */
export interface AuthRouteAction {
  kind: 'auth-route'
  guestTo: string
  authedTo: string
  label: string
}
export type LandingAction = AnchorAction | RouteAction | AuthRouteAction

export interface LandingStep {
  icon: IconName
  title: string
  text: string
}
/** Ровно три шага: 2 или 4 — ошибка tsc. */
export type ThreeSteps = readonly [LandingStep, LandingStep, LandingStep]
export interface LandingStepsPanel {
  title: string
  titleId: string
  steps: ThreeSteps
}

export interface LandingShotSource {
  media: string
  srcSet: string
  width: number
  height: number
}
export interface LandingScreenshot {
  src: string
  srcSet?: string
  width: number
  height: number
  sources?: readonly LandingShotSource[]
  alt: string
  caption: string
}

export interface LandingHeroConfig {
  eyebrow: string
  /** Строка — одна строка заголовка; объект — «строка + выделенная часть» (выделение с новой строки). */
  title: string | { lead: string; accent: string }
  intro: string
  primaryAction?: AnchorAction | RouteAction
  /** Якорь на заголовок секции 3 («Как сделать заказ» / «Как записаться»). */
  howTo: AnchorAction
}

export interface LandingCatalogFrameConfig {
  id: string
  ariaLabel: string
}

/** Необязательный медиа-блок после каталога. */
export interface LandingMediaConfig {
  src: string
  alt: string
  width: number
  height: number
  caption?: string
}

export interface LandingClientsConfig {
  eyebrow: string
  title: string
  titleId: string
  text: string
  actions: readonly [LandingAction] | readonly [LandingAction, LandingAction]
  stepsPanel: LandingStepsPanel
  list: { title: string; titleId: string; items: readonly [string, ...string[]]; note?: string }
  screenshot?: LandingScreenshot
}

export interface LandingBusinessConfig {
  eyebrow: string
  title: string
  titleId: string
  text: string
  actions: readonly [LandingAction, LandingAction]
  benefits: readonly [string, ...string[]]
  screenshot?: LandingScreenshot
  stepsPanel: LandingStepsPanel
}

export interface LandingPricingConfig {
  line: PricingLine
  /** Фраза перед «— от {цена}». */
  lead: string
}

export interface FaqItem {
  question: string
  /** Простой текст. Массив — абзацы. HTML не интерпретируется никогда. */
  answer: string | readonly [string, ...string[]]
  /** Необязательная ссылка после ответа (внутренний маршрут). */
  link?: { to: string; label: string }
}
/** Не меньше 6 — проверяет tsc; не больше 8 — проверяет тест конфига. */
export type FaqItems = readonly [FaqItem, FaqItem, FaqItem, FaqItem, FaqItem, FaqItem, ...FaqItem[]]

export interface LandingConfig {
  hero: LandingHeroConfig
  catalog: LandingCatalogFrameConfig
  media?: LandingMediaConfig
  clients: LandingClientsConfig
  business: LandingBusinessConfig
  pricing?: LandingPricingConfig
  faq: { items: FaqItems }
}
