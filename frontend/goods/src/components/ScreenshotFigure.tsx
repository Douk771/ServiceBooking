type ShotSource = { media: string; srcSet: string; width: number; height: number }

type Props = {
  src: string
  srcSet?: string
  width: number
  height: number
  sources?: ShotSource[]
  alt: string
  caption: string
  className?: string
  imgClassName?: string
}

/**
 * Screenshot with caption shared by «Для покупателей» and «Для бизнеса» (ARCHITECTURE_CYCLE30.md §30.4.2).
 * width/height are the CSS size of the 1x frame from screenshots.json, so the layout does not jump while it loads.
 */
export function ScreenshotFigure({ src, srcSet, width, height, sources, alt, caption, className, imgClassName }: Props) {
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
