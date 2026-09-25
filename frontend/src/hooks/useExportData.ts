import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import { format } from 'date-fns'
import { profileApi } from '../api/profile'

/**
 * `GET /api/profile/export` (US-38) — shared between ProfilePage and ConsentGate, since the endpoint
 * is in the 451 allow-list (API_CONTRACT.md §0.4) and must stay reachable from the blocking consent
 * screen too, not just from /profile.
 *
 * Cycle 16 (API_CONTRACT_CYCLE16.md §273.1, §276.2): the file may now additionally carry a
 * `guestDataGate` section. We only ever read `applied`/`explanation` off it to surface the same
 * server-written text the file itself contains — never derive anything from whether other sections
 * are present/empty (§272), and never fail the download just because the gate applied.
 */
interface GuestDataGateSection {
  applied: boolean
  explanation: string | null
}

export function useExportData() {
  const [exportError, setExportError] = useState('')
  const [gateNotice, setGateNotice] = useState<string | null>(null)

  const exportMut = useMutation({
    mutationFn: () => profileApi.exportData(),
    onMutate: () => {
      setExportError('')
      setGateNotice(null)
    },
    onSuccess: async (blob) => {
      // The gate section is read from the same blob that gets downloaded, not a second request —
      // there is exactly one call to the endpoint per click (§273.4's rate limit is per-request).
      try {
        // `blob.text()` isn't implemented by jsdom's `Blob` (only real browsers), and `Response`
        // doesn't recognise jsdom's `Blob` as its own — `FileReader` is the one API both jsdom and
        // real browsers implement consistently for reading a `Blob`'s contents as text.
        const text = await new Promise<string>((resolve, reject) => {
          const reader = new FileReader()
          reader.onload = () => resolve(String(reader.result))
          reader.onerror = () => reject(reader.error)
          reader.readAsText(blob)
        })
        const parsed = JSON.parse(text) as { guestDataGate?: GuestDataGateSection }
        if (parsed.guestDataGate?.applied) {
          setGateNotice(parsed.guestDataGate.explanation ?? null)
        }
      } catch {
        // Malformed JSON shouldn't block the download the user just triggered — the file itself is
        // still handed to them below, unparsed.
      }

      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `servicebooking-export-${format(new Date(), 'yyyy-MM-dd')}.json`
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    },
    onError: async (err: unknown) => {
      // With `responseType: 'blob'`, axios puts the error body in a Blob too — read it as text to
      // get the plain-text 429 message the server sent (API_CONTRACT.md §0.2, §8).
      if (err instanceof AxiosError && err.response?.data instanceof Blob) {
        const text = await err.response.data.text()
        setExportError(text || 'Не удалось скачать данные. Попробуйте снова.')
      } else {
        setExportError('Не удалось скачать данные. Попробуйте снова.')
      }
    },
  })

  return { exportMut, exportError, gateNotice }
}
