import { useEffect, useRef, useState } from 'react'
import { useCarousel } from '../../hooks/useCarousel'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import { orderPhotosForDisplay } from '../../utils/companyPhotos'
import { Icon } from '../ui/Icon'
import type { CompanyPhoto } from '../../types'

interface Props {
  photos: CompanyPhoto[]
  companyName: string
}

/**
 * ARCHITECTURE_CYCLE13.md §204/§211 (US-131, US-141…US-143). Renders from `company.photos` (`GET
 * /api/companies/{slug}`) — never fetches on its own, so it adds zero requests to the public page.
 *
 * The 2×2 mosaic + "+N" tile from cycle 10 is gone entirely, replaced by one carousel for any photo
 * count. Review finding from cycle 10 (`CompanyPhotoGallery.tsx:46-49` in the old mosaic version, kept
 * here per §211 US-143 rather than deleted): the mosaic only ever surfaced photos 2–5 on the public
 * page, and the "+N" tile on the last visible cell was a workaround for that ceiling, not a fix —
 * photos 6–10 (up to `MAX_PHOTOS`) had no way to be reached from this page at all. The carousel
 * removes the ceiling: every photo, however many there are, is one swipe/arrow-press/indicator-click
 * away, and the Lightbox it opens into can page through the same full set.
 */
export function CompanyPhotoGallery({ photos, companyName }: Props) {
  const [openIndex, setOpenIndex] = useState<number | null>(null)

  if (photos.length === 0) {
    return (
      <div className="w-full h-[220px] rounded-[18px] bg-cream-deep flex items-center justify-center">
        <Icon name="store" size={28} strokeWidth={1.6} className="text-gold-dark" />
      </div>
    )
  }

  // The exact same ordered array feeds both the carousel and the Lightbox, so their indices can
  // never point at two different photos (§211).
  const orderedPhotos = orderPhotosForDisplay(photos)

  return (
    <>
      <Carousel photos={orderedPhotos} companyName={companyName} onOpenSlide={setOpenIndex} />
      {/* §204: the Lightbox is a SIBLING of the carousel root, never its descendant. The slide track
          uses `transform: translateX(...)`, and `position: fixed` inside an ancestor with a
          `transform` stops being "relative to the viewport" and gets clipped by that ancestor — so a
          Lightbox nested inside the track would visually get trapped inside the 280px-tall gallery
          instead of covering the screen. */}
      {openIndex !== null && (
        <Lightbox photos={orderedPhotos} index={openIndex} companyName={companyName} onClose={() => setOpenIndex(null)} />
      )}
    </>
  )
}

function Carousel({
  photos,
  companyName,
  onOpenSlide,
}: {
  photos: CompanyPhoto[]
  companyName: string
  onOpenSlide: (index: number) => void
}) {
  const { index, next, prev, goTo, onKeyDown, touchHandlers } = useCarousel(photos.length)
  const single = photos.length === 1
  const slideRefs = useRef<(HTMLButtonElement | null)[]>([])

  // The slide that just became inactive gets `aria-hidden`/`tabIndex={-1}` (below) the moment `index`
  // changes — if focus was still sitting on it (ArrowLeft/ArrowRight pressed with a slide focused),
  // the focused element would end up hidden from the accessibility tree while still holding focus.
  // Move focus along to the newly active slide in that case (review finding, cycle 13).
  useEffect(() => {
    const active = document.activeElement
    if (active instanceof HTMLElement && slideRefs.current.includes(active) && active !== slideRefs.current[index]) {
      slideRefs.current[index]?.focus()
    }
  }, [index])

  return (
    // `isolate` (CSS `isolation: isolate`) gives everything inside its own stacking context, so no
    // z-index in here — arrows, indicators, future decoration — can ever climb above the logo row in
    // CompanyPage.tsx (§204/R10). The root itself gets NO z-index and NO transform/filter/contain —
    // any of those would clip a `position: fixed` Lightbox nested inside it (it isn't, but the rule
    // is "never add one here" precisely so that stays true).
    <div
      className="relative isolate h-[280px] overflow-hidden rounded-[18px] bg-cream-deep"
      tabIndex={single ? -1 : 0}
      role="group"
      aria-roledescription="карусель"
      aria-label={`Фотографии ${companyName}`}
      onKeyDown={single ? undefined : onKeyDown}
    >
      <div
        className="absolute inset-0 flex touch-pan-y transition-transform duration-300 motion-reduce:transition-none"
        style={{ transform: `translateX(-${index * 100}%)` }}
        {...(single ? {} : touchHandlers)}
      >
        {photos.map((p, i) => {
          // Keep a small window of slides actually fetching images — the active one plus its two
          // immediate (wrap-aware) neighbours — so ten photos on a mobile connection don't all load
          // at once, while still being reachable the instant the visitor pages to them (§211 R12).
          const distance = Math.min(Math.abs(i - index), photos.length - Math.abs(i - index))
          const inWindow = single || distance <= 1
          return (
            <button
              key={p.id}
              ref={(el) => {
                slideRefs.current[i] = el
              }}
              type="button"
              className="w-full h-full shrink-0 relative"
              onClick={() => onOpenSlide(i)}
              aria-label={`Фото ${i + 1} из ${photos.length}`}
              // Only the active slide is visible (the track is `translateX`'d out of view for the
              // rest) — keep the other 1..N-1 buttons out of both tab order and the accessibility
              // tree so keyboard/screen-reader users don't step through invisible slides (review
              // finding, cycle 13). The arrows/indicators below remain the way to reach them.
              tabIndex={i === index ? 0 : -1}
              aria-hidden={i !== index}
            >
              {inWindow && (
                <img
                  src={p.url}
                  srcSet={`${p.thumbnailUrl} 480w, ${p.url} 1600w`}
                  sizes="(max-width: 900px) 100vw, 870px"
                  alt={companyName}
                  loading={i === index ? 'eager' : 'lazy'}
                  className="w-full h-full object-cover"
                />
              )}
            </button>
          )
        })}
      </div>

      {!single && (
        <>
          {/* Siblings of the slide buttons, not their descendants — nesting a <button> inside a
              <button> is invalid HTML and is exactly how "the arrow also opens the lightbox" bugs
              happen (US-142). z-10 here is scoped by `isolate` above, see §204. */}
          <button
            type="button"
            onClick={prev}
            aria-label="Предыдущее фото"
            className="absolute z-10 top-1/2 -translate-y-1/2 left-2 w-11 h-11 rounded-full bg-ink/45 hover:bg-ink/65 text-cream flex items-center justify-center transition-colors"
          >
            <Icon name="chevron-left" size={22} strokeWidth={1.8} />
          </button>
          <button
            type="button"
            onClick={next}
            aria-label="Следующее фото"
            className="absolute z-10 top-1/2 -translate-y-1/2 right-2 w-11 h-11 rounded-full bg-ink/45 hover:bg-ink/65 text-cream flex items-center justify-center transition-colors"
          >
            <Icon name="chevron-right" size={22} strokeWidth={1.8} />
          </button>
          <div className="absolute z-10 bottom-3 left-1/2 -translate-x-1/2 flex items-center gap-1.5">
            {photos.map((p, i) => (
              <button
                key={p.id}
                type="button"
                onClick={() => goTo(i)}
                aria-label={`Показать фото ${i + 1}`}
                aria-current={i === index ? 'true' : undefined}
                className={
                  i === index
                    ? 'w-2.5 h-2.5 rounded-full bg-cream ring-2 ring-offset-1 ring-offset-ink/40 ring-cream transition-all'
                    : 'w-2 h-2 rounded-full bg-cream/55 hover:bg-cream/75 transition-all'
                }
              />
            ))}
          </div>
        </>
      )}

      <span className="sr-only" aria-live="polite">
        Фото {index + 1} из {photos.length}
      </span>
    </div>
  )
}

function Lightbox({
  photos,
  index,
  companyName,
  onClose,
}: {
  photos: CompanyPhoto[]
  index: number
  companyName: string
  onClose: () => void
}) {
  const [i, setI] = useState(index)
  const dismiss = useOverlayDismiss(onClose)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowRight') setI((v) => (v + 1) % photos.length)
      if (e.key === 'ArrowLeft') setI((v) => (v - 1 + photos.length) % photos.length)
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [photos.length])

  const photo = photos[i]

  return (
    <div className="fixed inset-0 bg-ink/85 z-50 flex items-center justify-center p-6" {...dismiss}>
      <button onClick={onClose} aria-label="Закрыть" className="absolute top-5 right-5 text-cream/80 hover:text-cream">
        <Icon name="x" size={26} strokeWidth={1.8} />
      </button>
      {photos.length > 1 && (
        <>
          <button
            onClick={() => setI((v) => (v - 1 + photos.length) % photos.length)}
            aria-label="Предыдущее фото"
            className="absolute left-4 text-cream/80 hover:text-cream"
          >
            <Icon name="chevron-left" size={30} strokeWidth={1.8} />
          </button>
          <button
            onClick={() => setI((v) => (v + 1) % photos.length)}
            aria-label="Следующее фото"
            className="absolute right-4 text-cream/80 hover:text-cream"
          >
            <Icon name="chevron-right" size={30} strokeWidth={1.8} />
          </button>
        </>
      )}
      <img
        src={photo.url}
        alt={companyName}
        loading="lazy"
        className="max-w-full max-h-full rounded-xl object-contain"
      />
    </div>
  )
}
