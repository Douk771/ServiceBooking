import { useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { formatFileSize, isPdf } from '../utils/paymentProof'
import { getStayErrorMessage } from '../utils/stayError'
import { InlineError } from './StatePanels'

interface Props {
  contentType: string
  sizeBytes: number
  purged: boolean
  title: string
  /** The booking-side route of the file (guest: by token, staff: by company and booking). Authorised by the caller. */
  fetchBlob: () => Promise<Blob>
}

/**
 * One payment proof: an image opens in a preview window, a PDF is downloaded (never opened inline — the server serves it as an
 * attachment, ARCHITECTURE_CYCLE37.md §37.8). The bytes come through the API client as a blob; there is no link with a token in an
 * `<a href>`. A file removed by the retention rule says so instead of offering a dead button.
 */
export function ProofFileButton({ contentType, sizeBytes, purged, title, fetchBlob }: Props) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [preview, setPreview] = useState<string | null>(null)

  if (purged) return <p className="text-sm text-muted">{title} — файл удалён по сроку хранения</p>

  const open = async () => {
    setBusy(true)
    setError('')
    try {
      const blob = await fetchBlob()
      const url = URL.createObjectURL(blob)
      if (isPdf(contentType)) {
        const a = document.createElement('a')
        a.href = url
        a.download = 'payment-proof.pdf'
        document.body.appendChild(a)
        a.click()
        a.remove()
        setTimeout(() => URL.revokeObjectURL(url), 10_000)
      } else {
        setPreview(url)
      }
    } catch (err) {
      setError(getStayErrorMessage(err, 'Не удалось открыть файл.'))
    } finally {
      setBusy(false)
    }
  }

  const close = () => {
    if (preview) URL.revokeObjectURL(preview)
    setPreview(null)
  }

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-ink">
          {title} <span className="text-muted">· {isPdf(contentType) ? 'PDF' : 'изображение'}, {formatFileSize(sizeBytes)}</span>
        </p>
        <Button type="button" variant="secondary" size="sm" loading={busy} onClick={() => void open()} className="min-h-[44px]">
          {isPdf(contentType) ? 'Скачать' : 'Посмотреть'}
        </Button>
      </div>
      {error && (
        <div className="mt-2">
          <InlineError>{error}</InlineError>
        </div>
      )}
      {preview && (
        <Modal title={title} onClose={close}>
          <img src={preview} alt={title} className="mx-auto max-h-[70vh] w-auto max-w-full rounded-xl" />
        </Modal>
      )}
    </div>
  )
}
