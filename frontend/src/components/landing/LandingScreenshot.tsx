import type { LandingScreenshot as Shot } from './types'

export const SCREENSHOT_PLACEHOLDER_TEXT = 'Здесь будут скриншоты'

interface Props {
  shot: Shot
  className?: string
  imgClassName?: string
}

/**
 * Скриншот с подписью (бывший goods ScreenshotFigure). width/height — CSS-размер кадра 1x,
 * чтобы вёрстка не прыгала при загрузке.
 */
export function LandingScreenshotFigure({ shot, className, imgClassName }: Props) {
  const { src, srcSet, width, height, sources, alt, caption } = shot
  return (
    <figure className={className}>
      <picture>
        {sources?.map((s) => (
          <source key={s.media} type="image/webp" media={s.media} srcSet={s.srcSet} width={s.width} height={s.height} />
        ))}
        <img
          src={src}
          srcSet={srcSet}
          width={width}
          height={height}
          alt={alt}
          loading="lazy"
          decoding="async"
          className={`block w-full h-auto rounded-[20px] border border-line bg-white shadow-soft ${imgClassName ?? ''}`.trim()}
        />
      </picture>
      <figcaption className="mt-3 text-[13px] text-muted text-center">{caption}</figcaption>
    </figure>
  )
}

/** Заглушка пустого слота скриншота: не img и не figure (ARCHITECTURE_CYCLE37.md §37.3.6). */
export function LandingScreenshotPlaceholder() {
  return (
    <div
      data-testid="landing-screenshot-placeholder"
      className="mx-auto w-full max-w-[320px] md:max-w-[340px] aspect-[390/600] rounded-[20px] border border-dashed border-line-strong bg-cream-deep flex items-center justify-center text-center px-6"
    >
      <p className="text-[15px] text-muted">{SCREENSHOT_PLACEHOLDER_TEXT}</p>
    </div>
  )
}
