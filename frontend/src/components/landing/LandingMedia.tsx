import type { LandingMediaConfig } from './types'

/** Необязательный медиа-блок после каталога: иллюстрация, не секция (без заголовка). */
export function LandingMedia({ config }: { config: LandingMediaConfig }) {
  return (
    <figure className="mt-16 md:mt-20">
      <img
        src={config.src}
        alt={config.alt}
        width={config.width}
        height={config.height}
        loading="lazy"
        decoding="async"
        className="block w-full h-auto aspect-[4/3] md:aspect-[21/9] object-cover rounded-[28px] border border-line"
      />
      {config.caption && <figcaption className="mt-3 text-[13px] text-muted text-center">{config.caption}</figcaption>}
    </figure>
  )
}
