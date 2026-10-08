import type { ReactElement, ReactNode } from 'react'
import { FaqSection } from './FaqSection'
import { LandingBusinessSection } from './LandingBusinessSection'
import { LandingCatalogFrame } from './LandingCatalogFrame'
import { LandingClientsSection } from './LandingClientsSection'
import { LandingHero } from './LandingHero'
import { LandingMedia } from './LandingMedia'
import { LandingPanelHero } from './LandingPanelHero'
import { LandingPricingSection } from './LandingPricingSection'
import { CONTAINER } from './classes'
import type { LandingConfig, LandingPanelConfig } from './types'

/**
 * Пропсы: обычный конфиг не принимает heroAside, конфиг с шапкой-панелью требует его
 * (ARCHITECTURE_CYCLE41.md §41.3.2).
 */
export type ServiceLandingProps =
  | { config: LandingConfig; catalog: ReactNode; heroAside?: undefined }
  | { config: LandingPanelConfig; catalog: ReactNode; heroAside: ReactElement }

type PanelProps = Extract<ServiceLandingProps, { config: LandingPanelConfig }>

function isPanelProps(p: ServiceLandingProps): p is PanelProps {
  return p.config.hero.layout === 'panel'
}

/** Секции 2-6: общий фрагмент обеих веток разметки. */
function LandingSections({ config, catalog }: { config: LandingConfig | LandingPanelConfig; catalog: ReactNode }) {
  // Заглушка скриншота — один раз на страницу, в первом пустом слоте (секция 3, затем 4) — §38.3.6.
  const clientsPlaceholder = !config.clients.screenshot
  const businessPlaceholder = !clientsPlaceholder && !config.business.screenshot
  return (
    <>
      <LandingCatalogFrame config={config.catalog}>{catalog}</LandingCatalogFrame>
      {config.media && <LandingMedia config={config.media} />}
      <LandingClientsSection config={config.clients} placeholder={clientsPlaceholder} />
      <LandingBusinessSection config={config.business} placeholder={businessPlaceholder} />
      {config.pricing && <LandingPricingSection config={config.pricing} />}
      <FaqSection items={config.faq.items} />
    </>
  )
}

/**
 * Единый шаблон главной сервиса (ARCHITECTURE_CYCLE38.md §38.3.2). Порядок секций, контейнер и отступы — только здесь.
 * Футер в шаблон не входит.
 */
export function ServiceLanding(props: ServiceLandingProps) {
  if (isPanelProps(props)) {
    return (
      <main className="pb-10">
        <LandingPanelHero config={props.config.hero} aside={props.heroAside} />
        <div className={CONTAINER}>
          <LandingSections config={props.config} catalog={props.catalog} />
        </div>
      </main>
    )
  }
  return (
    <main className="max-w-[1180px] mx-auto px-4 sm:px-8 pt-10 md:pt-16 pb-10">
      <LandingHero config={props.config.hero} />
      <LandingSections config={props.config} catalog={props.catalog} />
    </main>
  )
}
