import { useCallback, useEffect, useRef, useState } from 'react'
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
 */
export function LandingScreenshotCarousel({ carousel, className }: { carousel: Carousel; className?: string }) {
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

  return (
    <div
      role="region"
      aria-roledescription="карусель"
      aria-label={label}
      className={className}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setFocused(false)
      }}
    >
      <div
        className="grid touch-pan-y"
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
              <LandingScreenshotFigure shot={shot} />
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
        <div className="flex items-center">
          {slides.map((shot, i) => (
            <button
              key={shot.src}
              type="button"
              onClick={() => go(i)}
              aria-label={`Кадр ${i + 1}: ${shot.caption}`}
              aria-current={i === index ? 'true' : undefined}
              className="w-6 h-9 flex items-center justify-center"
            >
              <span
                className={`block h-2 rounded-full transition-all ${i === index ? 'w-5 bg-ink' : 'w-2 bg-line-strong'}`}
                aria-hidden
              />
            </button>
          ))}
        </div>
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
  )
}
