import { useCallback, useEffect, useRef, useState } from 'react'
import { companyPhotosApi } from '../../api/companyPhotos'
import type { CompanyPhoto } from '../../types'
import { getUploadErrorMessage } from '../../utils/uploadError'
import { isTransientUploadError, overLimitText, photoRejectText, planPhotoBatch } from '../../utils/photoBatch'

export type PhotoUploadStatus = 'queued' | 'uploading' | 'done' | 'error'

export interface PhotoUploadItem {
  key: string
  name: string
  status: PhotoUploadStatus
  error?: string
  progress?: number
  /** Failed with a worth-retrying error (429, network, 5xx) — feeds "Повторить неудавшиеся". */
  transient?: boolean
  /** Dropped before sending (type/size) — was never part of the batch total. */
  rejected?: boolean
}

interface Options {
  /** Upsert into the gallery cache by id. */
  onUploaded: (photo: CompanyPhoto) => void
  /** Once per batch: invalidate caches, notify the host. */
  onBatchSettled: () => void
  /** Cycle 37 (ARCHITECTURE_CYCLE37.md §37.14.2): another upload route (a house's photos). Default — the company gallery. */
  upload?: (file: File, onProgress: (percent: number) => void) => Promise<CompanyPhoto>
  /** Text of a failed upload. Default — `getUploadErrorMessage`. */
  errorMessage?: (err: unknown) => string
  /** Gallery limit named in the «does not fit» message. Default — `PHOTO_MAX_PHOTOS`. */
  maxPhotos?: number
}

/**
 * ARCHITECTURE_CYCLE31.md §31.9.2 — the multi-file upload queue. Files go to the server STRICTLY one by one in
 * selection order (R-2: the server appends by `count` under a lock, so order is the client's job); one failed
 * file never stops the rest. No cancellation and no background upload (SPEC §3): on unmount the running request
 * finishes, the next file does not start and no state is touched.
 */
export function usePhotoBatchUpload(companyId: string, opts: Options) {
  const [items, setItems] = useState<PhotoUploadItem[]>([])
  const [overLimitMessage, setOverLimitMessage] = useState<string | null>(null)
  const [running, setRunning] = useState(false)
  const runningRef = useRef(false)
  const mountedRef = useRef(true)
  const filesRef = useRef(new Map<string, File>())
  const itemsRef = useRef<PhotoUploadItem[]>([])
  const optsRef = useRef(opts)
  optsRef.current = opts
  const batchSeq = useRef(0)

  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  const commit = useCallback((next: PhotoUploadItem[]) => {
    itemsRef.current = next
    if (mountedRef.current) setItems(next)
  }, [])

  const patch = useCallback(
    (key: string, change: Partial<PhotoUploadItem>) => {
      commit(itemsRef.current.map((i) => (i.key === key ? { ...i, ...change } : i)))
    },
    [commit],
  )

  const run = useCallback(
    async (keys: string[]) => {
      runningRef.current = true
      if (mountedRef.current) setRunning(true)
      let attempted = false
      try {
        for (const key of keys) {
          if (!mountedRef.current) break
          const file = filesRef.current.get(key)
          if (!file) continue
          attempted = true
          patch(key, { status: 'uploading', progress: 0, error: undefined, transient: false })
          try {
            const onProgress = (progress: number) => patch(key, { progress })
            const photo = await (optsRef.current.upload
              ? optsRef.current.upload(file, onProgress)
              : companyPhotosApi.upload(companyId, file, onProgress))
            optsRef.current.onUploaded(photo)
            patch(key, { status: 'done', progress: 100 })
          } catch (err) {
            patch(key, { status: 'error', error: (optsRef.current.errorMessage ?? getUploadErrorMessage)(err), transient: isTransientUploadError(err) })
          }
        }
      } finally {
        runningRef.current = false
        if (mountedRef.current) setRunning(false)
        if (attempted) optsRef.current.onBatchSettled()
      }
    },
    [companyId, patch],
  )

  const start = useCallback(
    (files: File[], remainingSlots: number) => {
      if (runningRef.current || files.length === 0) return
      const plan = planPhotoBatch(files, remainingSlots)
      const acceptedSet = new Set(plan.accepted)
      const rejectedReason = new Map(plan.rejected.map((r) => [r.file, r.reason]))
      const batch = ++batchSeq.current
      filesRef.current = new Map()
      const next: PhotoUploadItem[] = []
      const queue: string[] = []
      files.forEach((file, i) => {
        const key = `${batch}-${i}`
        const reason = rejectedReason.get(file)
        if (reason) {
          next.push({ key, name: file.name, status: 'error', error: photoRejectText(reason), rejected: true })
        } else if (acceptedSet.has(file)) {
          filesRef.current.set(key, file)
          next.push({ key, name: file.name, status: 'queued' })
          queue.push(key)
        }
      })
      commit(next)
      setOverLimitMessage(plan.overLimit.length > 0 ? overLimitText(plan.overLimit, optsRef.current.maxPhotos) : null)
      void run(queue)
    },
    [commit, run],
  )

  const retryFailed = useCallback(() => {
    if (runningRef.current) return
    const keys = itemsRef.current.filter((i) => i.status === 'error' && i.transient).map((i) => i.key)
    if (keys.length === 0) return
    commit(itemsRef.current.map((i) => (keys.includes(i.key) ? { ...i, status: 'queued', error: undefined } : i)))
    void run(keys)
  }, [commit, run])

  const clear = useCallback(() => {
    if (runningRef.current) return
    filesRef.current = new Map()
    commit([])
    setOverLimitMessage(null)
  }, [commit])

  return {
    items,
    overLimitMessage,
    running,
    done: items.filter((i) => i.status === 'done').length,
    total: items.filter((i) => !i.rejected).length,
    start,
    retryFailed,
    clear,
  }
}
