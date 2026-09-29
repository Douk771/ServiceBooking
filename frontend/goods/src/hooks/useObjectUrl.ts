import { useEffect, useState } from 'react'

/** Blob → object URL that is revoked when the blob changes or the component unmounts. */
export function useObjectUrl(blob: Blob | undefined | null): string | null {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    if (!blob) {
      setUrl(null)
      return
    }
    const u = URL.createObjectURL(blob)
    setUrl(u)
    return () => URL.revokeObjectURL(u)
  }, [blob])
  return url
}
