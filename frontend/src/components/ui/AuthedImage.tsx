import { useAuthedImage } from '../../hooks/useAuthedImage'
import { Icon } from './Icon'

interface AuthedImageProps {
  photoId: string
  variant: 'full' | 'thumb'
  alt: string
  className?: string
  onClick?: () => void
  /** 'cover' fills a fixed-size box (thumbnails); 'contain' fits the whole image (viewer modal). */
  fit?: 'cover' | 'contain'
}

/**
 * `<img>` for a private client-note photo (US-18). See `useAuthedImage` for why this can't be a plain
 * `<img src="/api/client-notes/photos/{id}">`.
 */
export function AuthedImage({ photoId, variant, alt, className = '', onClick, fit = 'cover' }: AuthedImageProps) {
  const { ref, src, isError } = useAuthedImage(photoId, variant)

  return (
    <div
      ref={ref as React.RefObject<HTMLDivElement>}
      onClick={onClick}
      className={`relative overflow-hidden bg-cream-deep ${fit === 'contain' ? 'flex items-center justify-center' : ''} ${onClick ? 'cursor-pointer' : ''} ${className}`}
    >
      {src ? (
        <img
          src={src}
          alt={alt}
          loading="lazy"
          className={fit === 'contain' ? 'max-w-full max-h-full object-contain' : 'w-full h-full object-cover'}
        />
      ) : isError ? (
        <div className="w-full h-full flex items-center justify-center text-muted">
          <Icon name="alert-circle" size={16} strokeWidth={1.6} />
        </div>
      ) : (
        <div className="w-full h-full animate-pulse flex items-center justify-center text-muted">
          <Icon name="image" size={16} strokeWidth={1.5} />
        </div>
      )}
    </div>
  )
}
