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

/** Карусель кадров: листается сама, с кнопками и точками. Не меньше двух кадров — один кадр задаётся без карусели. */
export interface LandingScreenshotCarousel {
  /** Имя карусели для экранного диктора, например «Как проходит запись». */
  label: string
  slides: readonly [LandingScreenshot, LandingScreenshot, ...LandingScreenshot[]]
}
export type LandingShot = LandingScreenshot | LandingScreenshotCarousel

/** Порядок секций — данные конфига. Закрытый список: две известные раскладки (ARCHITECTURE_CYCLE46 §46.3). */
export type LandingOrder = 'catalog-first' | 'business-first'

/** Вторичная строка первого экрана для клиента: «{lead} <a>{action.label}</a>». */
export interface LandingHeroClientLink {
  lead: string
  action: AnchorAction
}

export interface LandingHeroConfig {
  /** Обычная шапка. Поле есть только для того, чтобы конфиг с layout: 'panel' нельзя было передать как обычный. */
  layout?: never
  eyebrow: string
  /** Строка — одна строка заголовка; объект — «строка + выделенная часть» (выделение с новой строки). */
  title: string | { lead: string; accent: string }
  intro: string
  primaryAction?: LandingAction
  /** Якорь на заголовок секции 3 («Как сделать заказ» / «Как записаться»). */
  howTo: AnchorAction
  clientLink?: LandingHeroClientLink
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
  screenshot?: LandingShot
}

/** Секция клиентов в раскладке 'business-first': кнопок и панели шагов нет. */
export interface LandingClientsShowcaseConfig {
  eyebrow: string
  title: string
  titleId: string
  text: string
  actions?: never
  stepsPanel?: never
  list: { title: string; titleId: string; items: readonly [string, ...string[]]; note?: string }
  screenshot?: LandingShot
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

interface LandingConfigCommon {
  catalog: LandingCatalogFrameConfig
  business: LandingBusinessConfig
  pricing?: LandingPricingConfig
  faq: { items: FaqItems }
}

/** Раскладка «каталог первым» («Заказы», «Дома», «Бани»); order можно не писать. */
export interface CatalogFirstLandingConfig extends LandingConfigCommon {
  order?: 'catalog-first'
  hero: LandingHeroConfig
  media?: LandingMediaConfig
  clients: LandingClientsConfig
}

/** Раскладка «Записи»: герой, бизнес, клиенты, шаги, тарифы, FAQ, каталог. */
export interface BusinessFirstLandingConfig extends LandingConfigCommon {
  order: 'business-first'
  hero: LandingHeroConfig & { primaryAction: LandingAction; clientLink: LandingHeroClientLink }
  media?: never
  clients: LandingClientsShowcaseConfig
}

export type LandingConfig = CatalogFirstLandingConfig | BusinessFirstLandingConfig

/** Закрытый список фоновых рисунков шапки с панелью. Новый мотив = новая строка тут + SVG в LandingHeroBackdrop.tsx. */
export type HeroBackdrop = 'none' | 'mountains'

/** Короткий факт под абзацем шапки; иконка декоративная (aria-hidden). */
export interface HeroFact {
  icon: IconName
  text: string
}
/** От 0 до 3 фактов: 4 — ошибка tsc. */
export type HeroFacts =
  | readonly []
  | readonly [HeroFact]
  | readonly [HeroFact, HeroFact]
  | readonly [HeroFact, HeroFact, HeroFact]

/** Шапка с боковой панелью (ARCHITECTURE_CYCLE41.md §41.3.1). Основной кнопки нет: её роль играет панель (слот heroAside). */
export interface LandingPanelHeroConfig {
  layout: 'panel'
  eyebrow: string
  title: string | { lead: string; accent: string }
  intro: string
  /** Якорь на заголовок секции 3. */
  howTo: AnchorAction
  facts: HeroFacts
  backdrop: HeroBackdrop
}

/** Конфиг главной с шапкой-панелью: только раскладка 'catalog-first', кроме hero. */
export type LandingPanelConfig = Omit<CatalogFirstLandingConfig, 'hero'> & { hero: LandingPanelHeroConfig }
