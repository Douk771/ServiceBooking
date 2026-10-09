import type { HeroBackdrop } from './types'

const SVG_CLASS = 'pointer-events-none absolute inset-x-0 bottom-0 h-16 w-full md:h-24 text-line-strong'

const BACKDROPS: Record<Exclude<HeroBackdrop, 'none'>, () => JSX.Element> = {
  mountains: () => (
    <svg aria-hidden="true" focusable="false" viewBox="0 0 1200 120" preserveAspectRatio="none" className={SVG_CLASS}>
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth="1.5"
        vectorEffect="non-scaling-stroke"
        opacity="0.6"
        d="M0 80 L110 50 L200 70 L320 24 L430 66 L540 34 L660 74 L780 28 L890 62 L1000 40 L1110 70 L1200 56"
      />
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth="1.5"
        vectorEffect="non-scaling-stroke"
        d="M0 96 L140 60 L230 84 L360 30 L470 78 L590 44 L700 88 L820 36 L930 70 L1040 50 L1200 92"
      />
    </svg>
  ),
}

/** Декоративный фон шапки с панелью: инлайн-SVG без текста и запросов (ARCHITECTURE_CYCLE41.md §41.3.3). */
export function LandingHeroBackdrop({ kind }: { kind: Exclude<HeroBackdrop, 'none'> }) {
  const Backdrop = BACKDROPS[kind]
  return Backdrop ? <Backdrop /> : null
}
