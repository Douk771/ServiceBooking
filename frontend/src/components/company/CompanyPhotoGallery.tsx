import { useEffect, useState } from 'react'
import { useOverlayDismiss } from '../../hooks/useOverlayDismiss'
import { Icon } from '../ui/Icon'
import type { CompanyPhoto } from '../../types'

interface Props {
  photos: CompanyPhoto[]
  companyName: string
}

/**
 * ARCHITECTURE_CYCLE10.md §109.2/§109.3 (US-126). Renders from `company.photos` (`GET
 * /api/companies/{slug}`) — never fetches on its own, so it adds zero requests to the public page.
 * Cover (`position === 0`/`isCover`) is the big tile, the rest a plain grid; clicking any of them
 * opens a full-size lightbox with the same dismiss convention as the rest of the product
 * (`useOverlayDismiss` — Esc/click-outside) plus keyboard arrows.
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

  const cover = photos.find((p) => p.isCover) ?? photos[0]
  const rest = photos.filter((p) => p.id !== cover.id)

  return (
    <>
      <div className="grid grid-cols-3 grid-rows-2 gap-1.5 rounded-[18px] overflow-hidden h-[280px]">
        <button
          type="button"
          className="col-span-2 row-span-2 relative"
          onClick={() => setOpenIndex(photos.indexOf(cover))}
        >
          <img
            src={cover.thumbnailUrl}
            alt={companyName}
            loading="lazy"
            className="w-full h-full object-cover hover:opacity-90 transition-opacity"
          />
        </button>
        {rest.slice(0, 4).map((p) => (
          <button key={p.id} type="button" className="relative" onClick={() => setOpenIndex(photos.indexOf(p))}>
            <img
              src={p.thumbnailUrl}
              alt={companyName}
              loading="lazy"
              className="w-full h-full object-cover hover:opacity-90 transition-opacity"
            />
          </button>
        ))}
      </div>

      {openIndex !== null && (
        <Lightbox photos={photos} index={openIndex} companyName={companyName} onClose={() => setOpenIndex(null)} />
      )}
    </>
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
      <img src={photo.url} alt={companyName} className="max-w-full max-h-full rounded-xl object-contain" />
    </div>
  )
}
