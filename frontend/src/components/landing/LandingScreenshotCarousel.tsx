import { type ReactNode, useCallback, useEffect, useRef, useState } from 'react'
import { Icon } from '../ui/Icon'
import { LandingScreenshotFigure } from './LandingScreenshot'
import type { LandingScreenshotCarousel as Carousel } from './types'

/** Пауза между кадрами при автолистании. */
export const CAROUSEL_INTERVAL_MS = 4500
const SWIPE_PX = 40

function prefersReducedMotion() {
  return typeof window !== 'undefined' && !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
}

/**
 * Карусель скриншотов: кадры лежат друг на друге в одной ячейке сетки (по верху), поэтому высота блока — по самому высокому кадру
 * и при смене кадра вёрстка не прыгает. Листается сама; останавливается при наведении, фокусе внутри и по кнопке
 * «Пауза» (WCAG 2.2.2), а при «уменьшить движение» в системе не листается вовсе. Листать можно стрелками, точками и свайпом.
 * `intro` — шапка секции над шагами в левой колонке: кадр справа центруется по шапке и шагам вместе.
 */
export function LandingScreenshotCarousel({ carousel, className, intro }: { carousel: Carousel; className?: string; intro?: ReactNode }) {
  const { slides, label } = carousel
  const count = slides.length
  const [index, setIndex] = useState(0)
  const [playing, setPlaying] = useState(() => !prefersReducedMotion())
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const swipeStart = useRef<number | null>(null)

  const go = useCallback((i: number) => setIndex(((i % count) + count) % count), [count])

  // Таймер перезапускается после каждой смены кадра — ручное листание не обрывается автоматическим через миг.
  useEffect(() => {
    if (!playing || hovered || focused) return
    const t = window.setTimeout(() => go(index + 1), CAROUSEL_INTERVAL_MS)
    return () => window.clearTimeout(t)
  }, [playing, hovered, focused, index, go])

  const title = (c: string) => c.replace(/^\d+\.\s*/, '')

  return (
    <div
      role="region"
      aria-roledescription="карусель"
      aria-label={label}
      className={`grid gap-5 md:grid-cols-[minmax(0,1fr)_auto] md:gap-12 lg:gap-16 md:items-center ${className ?? ''}`.trim()}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setFocused(false)
      }}
    >
      <div className="min-w-0 md:order-1">
        {intro && <div className="mb-5 md:mb-8">{intro}</div>}
        <p className="hidden md:block text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark mb-4">{label}</p>
        <ol className="list-none flex gap-2 overflow-x-auto md:overflow-visible md:flex-col md:gap-1.5 pb-1 md:pb-0">
          {slides.map((shot, i) => {
            const on = i === index
            return (
              <li key={shot.src} className="shrink-0 md:shrink">
                <button
                  type="button"
                  onClick={() => go(i)}
                  aria-label={`Кадр ${i + 1}: ${shot.caption}`}
                  aria-current={on ? 'true' : undefined}
                  className={`w-full text-left flex items-start gap-3 rounded-full md:rounded-2xl px-3.5 py-2 md:px-4 md:py-3 border transition-colors ${
                    on ? 'bg-ink text-cream border-ink md:bg-white md:text-ink md:border-line-strong md:shadow-soft' : 'bg-white text-ink-soft border-line hover:bg-cream-deep'
                  }`}
                >
                  <span aria-hidden className="text-[14px] font-semibold md:w-6 md:h-6 md:rounded-full md:bg-cream-deep md:flex md:items-center md:justify-center md:shrink-0 md:text-[13px]">
                    {i + 1}
                  </span>
                  <span aria-hidden className="hidden md:block">
                    <span className="block text-[16px] font-medium text-ink">{title(shot.caption)}</span>
                    {on && <span className="block mt-1 text-[14px] leading-[1.5] text-ink-soft">{shot.alt}</span>}
                  </span>
                </button>
              </li>
            )
          })}
        </ol>
        <p aria-hidden className="md:hidden mt-3 text-[15px] font-medium text-ink">{title(slides[index].caption)}</p>
      </div>

      <div className="mx-auto md:order-2 min-w-0">
        <div
          className="grid touch-pan-y justify-items-center"
          aria-live={playing && !hovered && !focused ? 'off' : 'polite'}
          onPointerDown={(e) => {
            swipeStart.current = e.clientX
          }}
          onPointerUp={(e) => {
            if (swipeStart.current === null) return
            const dx = e.clientX - swipeStart.current
            swipeStart.current = null
            if (Math.abs(dx) >= SWIPE_PX) go(index + (dx < 0 ? 1 : -1))
          }}
        >
          {slides.map((shot, i) => {
            const active = i === index
            return (
              <div
                key={shot.src}
                role="group"
                aria-roledescription="слайд"
                aria-label={`${i + 1} из ${count}`}
                aria-hidden={!active}
                data-testid="carousel-slide"
                data-active={active || undefined}
                className={`[grid-area:1/1] self-start transition-opacity duration-500 motion-reduce:transition-none ${
                  active ? 'opacity-100' : 'opacity-0 invisible'
                }`}
              >
                <LandingScreenshotFigure
                  shot={shot}
                  className="[&_figcaption]:sr-only"
                  imgClassName="!w-auto max-w-full !h-[min(60vh,540px)] object-contain !rounded-[28px] border-[6px] !border-ink/90"
                />
              </div>
            )
          })}
        </div>

        <div className="mt-4 flex items-center justify-center gap-2">
          <button
            type="button"
            onClick={() => go(index - 1)}
            aria-label="Предыдущий кадр"
            className="w-9 h-9 rounded-full border border-line bg-white text-ink flex items-center justify-center hover:bg-cream-deep"
          >
            <Icon name="chevron-left" size={16} aria-hidden />
          </button>
          <span className="text-[13px] text-muted tabular-nums px-1" aria-hidden>
            {index + 1} / {count}
          </span>
          <button
            type="button"
            onClick={() => go(index + 1)}
            aria-label="Следующий кадр"
            className="w-9 h-9 rounded-full border border-line bg-white text-ink flex items-center justify-center hover:bg-cream-deep"
          >
            <Icon name="chevron-right" size={16} aria-hidden />
          </button>
          <button
            type="button"
            onClick={() => setPlaying((p) => !p)}
            aria-label={playing ? 'Остановить автолистание' : 'Листать автоматически'}
            className="ml-1 w-9 h-9 rounded-full text-ink-soft flex items-center justify-center hover:text-ink"
          >
            <Icon name={playing ? 'pause' : 'play'} size={14} aria-hidden />
          </button>
        </div>
      </div>
    </div>
  )
}
