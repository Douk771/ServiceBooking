import type { LandingMediaConfig } from './types'

/**
 * Необязательный медиа-блок после каталога: иллюстрация, не секция (без заголовка).
 * Пропорции фото естественные (портрет не режется): на мобильном фото целиком,
 * на широком экране — по центру с ограничением высоты, подпись рядом.
 */
export function LandingMedia({ config }: { config: LandingMediaConfig }) {
  return (
    <figure className="mt-16 md:mt-20 flex flex-col md:flex-row items-center justify-center gap-6 md:gap-10">
      <img
        src={config.src}
        alt={config.alt}
        width={config.width}
        height={config.height}
        loading="lazy"
        decoding="async"
        className="block h-auto w-auto max-w-full max-h-[70vh] md:max-h-[560px] object-contain rounded-[28px] border border-line"
      />
      {config.caption && <figcaption className="text-[14px] text-muted text-center md:text-left md:max-w-[280px]">{config.caption}</figcaption>}
    </figure>
  )
}
