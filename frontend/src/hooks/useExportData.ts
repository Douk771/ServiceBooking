import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import { format } from 'date-fns'
import { profileApi } from '../api/profile'

/**
 * `GET /api/profile/export` (US-38) — shared between ProfilePage and ConsentGate, since the endpoint
 * is in the 451 allow-list (API_CONTRACT.md §0.4) and must stay reachable from the blocking consent
 * screen too, not just from /profile.
 */
export function useExportData() {
  const [exportError, setExportError] = useState('')

  const exportMut = useMutation({
    mutationFn: () => profileApi.exportData(),
    onMutate: () => setExportError(''),
    onSuccess: (blob) => {
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

  return { exportMut, exportError }
}
