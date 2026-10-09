import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { getStayErrorMessage } from '@/utils/slots/slotError'
import { ErrorState, Skeleton } from '@/components/slots/ui/StatePanels'

/**
 * QR code of a page (company or house): the PNG comes through the API client as a blob (the endpoint needs the token), is shown
 * and can be downloaded as the same blob. The code is for the owner's ads and the table on the porch (US-37-09).
 */
export function QrDialog({ title, fileName, load, onClose }: { title: string; fileName: string; load: () => Promise<Blob>; onClose: () => void }) {
  const [url, setUrl] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let revoked = false
    let created: string | null = null
    setError('')
    setUrl(null)
    load()
      .then((blob) => {
        if (revoked) return
        created = URL.createObjectURL(blob)
        setUrl(created)
      })
      .catch((e) => !revoked && setError(getStayErrorMessage(e, 'Не удалось получить QR-код.')))
    return () => {
      revoked = true
      if (created) URL.revokeObjectURL(created)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `load` is a fresh closure on every render; a retry is explicit
  }, [attempt])

  return (
    <Modal title={title} onClose={onClose}>
      {error ? (
        <ErrorState message={error} onRetry={() => setAttempt((n) => n + 1)} />
      ) : !url ? (
        <Skeleton className="mx-auto h-64 w-64" />
      ) : (
        <div className="flex flex-col items-center gap-4">
          <img src={url} alt={`QR-код: ${title}`} className="h-64 w-64 rounded-xl border border-line bg-white p-2" />
          <a
            href={url}
            download={fileName}
            className="inline-flex min-h-[44px] items-center justify-center rounded-full bg-ink px-6 text-sm font-semibold !text-cream hover:bg-ink/90"
          >
            Скачать PNG
          </a>
        </div>
      )}
      <div className="mt-4 flex justify-end">
        <Button variant="secondary" className="min-h-[44px]" onClick={onClose}>
          Закрыть
        </Button>
      </div>
    </Modal>
  )
}
