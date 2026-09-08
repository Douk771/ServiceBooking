import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { clientNotesApi } from '../api/clientNotes'

/**
 * Loads a private client-note photo (US-18, US-19) as a blob and exposes it as an object URL.
 *
 * Private photos require the `Authorization` header, which a plain `<img src="/api/...">` never sends
 * (the token lives in `localStorage`, not a cookie) — so the fetch goes through the same axios instance
 * as everything else, `responseType: 'blob'`, and the result is turned into `URL.createObjectURL(blob)`
 * (ARCHITECTURE.md §12.2).
 *
 * Real laziness comes from an `IntersectionObserver` on the returned `ref`, not from the `loading="lazy"`
 * attribute (which is meaningless once `src` is a blob URL that's already resolved) — the request isn't
 * issued at all until the element enters the viewport. Once fetched, react-query caches the blob
 * (`staleTime: Infinity`) so scrolling back into view doesn't refetch.
 */
export function useAuthedImage(photoId: string, variant: 'full' | 'thumb') {
  const ref = useRef<HTMLElement | null>(null)
  const [isVisible, setIsVisible] = useState(false)

  useEffect(() => {
    const el = ref.current
    if (!el || isVisible) return
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) setIsVisible(true)
      },
      { rootMargin: '150px' },
    )
    observer.observe(el)
    return () => observer.disconnect()
  }, [isVisible])

  const {
    data: blob,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ['note-photo', photoId, variant],
    queryFn: () => clientNotesApi.getPhotoBlob(photoId, variant),
    enabled: isVisible,
    staleTime: Infinity,
  })

  const [src, setSrc] = useState<string | undefined>(undefined)
  useEffect(() => {
    if (!blob) return
    const objectUrl = URL.createObjectURL(blob)
    setSrc(objectUrl)
    return () => URL.revokeObjectURL(objectUrl)
  }, [blob])

  return { ref, src, isLoading: isVisible && isLoading, isError }
}
