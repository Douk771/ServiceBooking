import type { ReactNode } from 'react'
import { SECTION } from './classes'
import type { LandingCatalogFrameConfig } from './types'

/** Рамка вокруг каталога сервиса; содержимое рисует сервис. 'top' — первым после героя, 'end' — последней секцией. */
export function LandingCatalogFrame({
  config,
  children,
  placement,
}: {
  config: LandingCatalogFrameConfig
  children: ReactNode
  placement: 'top' | 'end'
}) {
  return (
    <section
      id={config.id}
      aria-label={config.ariaLabel}
      className={placement === 'top' ? 'mt-8 scroll-mt-24' : `${SECTION} scroll-mt-24`}
    >
      {children}
    </section>
  )
}
