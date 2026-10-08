import type { ReactNode } from 'react'
import type { LandingCatalogFrameConfig } from './types'

/** Секция 2: рамка вокруг каталога сервиса; содержимое рисует сервис. */
export function LandingCatalogFrame({ config, children }: { config: LandingCatalogFrameConfig; children: ReactNode }) {
  return (
    <section id={config.id} aria-label={config.ariaLabel} className="mt-8 scroll-mt-24">
      {children}
    </section>
  )
}
