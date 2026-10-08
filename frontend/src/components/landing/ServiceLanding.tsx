import type { ReactNode } from 'react'
import { FaqSection } from './FaqSection'
import { LandingBusinessSection } from './LandingBusinessSection'
import { LandingCatalogFrame } from './LandingCatalogFrame'
import { LandingClientsSection } from './LandingClientsSection'
import { LandingHero } from './LandingHero'
import { LandingMedia } from './LandingMedia'
import { LandingPricingSection } from './LandingPricingSection'
import type { LandingConfig } from './types'

/**
 * Единый шаблон главной сервиса (ARCHITECTURE_CYCLE38.md §38.3.2). Порядок секций, контейнер и отступы — только здесь.
 * Футер в шаблон не входит.
 */
export function ServiceLanding({ config, catalog }: { config: LandingConfig; catalog: ReactNode }) {
  // Заглушка скриншота — один раз на страницу, в первом пустом слоте (секция 3, затем 4) — §38.3.6.
  const clientsPlaceholder = !config.clients.screenshot
  const businessPlaceholder = !clientsPlaceholder && !config.business.screenshot
  return (
    <main className="max-w-[1180px] mx-auto px-4 sm:px-8 pt-10 md:pt-16 pb-10">
      <LandingHero config={config.hero} />
      <LandingCatalogFrame config={config.catalog}>{catalog}</LandingCatalogFrame>
      {config.media && <LandingMedia config={config.media} />}
      <LandingClientsSection config={config.clients} placeholder={clientsPlaceholder} />
      <LandingBusinessSection config={config.business} placeholder={businessPlaceholder} />
      {config.pricing && <LandingPricingSection config={config.pricing} />}
      <FaqSection items={config.faq.items} />
    </main>
  )
}
