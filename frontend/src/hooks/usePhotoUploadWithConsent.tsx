import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import { clientNotesApi } from '../api/clientNotes'
import { clientConsentsApi } from '../api/clientConsents'
import { getUploadErrorMessage } from '../utils/uploadError'
import { ClientConsentModal } from '../components/clientNotes/ClientConsentModal'

// API_CONTRACT_CYCLE5.md §44.3 — the exact plain-text body the server sends when photofixation
// consent is missing for this (client, company) pair. Matched by substring, same convention as the
// rest of the upload error mapping (utils/uploadError.ts).
const CONSENT_REQUIRED_MARKER = 'подтвердить согласие клиента на фотофиксацию'

function isConsentRequiredError(e: unknown): boolean {
  const ax = e as AxiosError
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''
  return ax?.response?.status === 400 && body.includes(CONSENT_REQUIRED_MARKER)
}

interface Options {
  /**
   * Required together with `clientKey` to react to a missing-consent 400 (§44.3, T5-F6) by opening
   * the consent step and resuming, instead of just failing the upload. Without them, a missing-consent
   * 400 falls back to the plain error message like any other upload failure.
   */
  companyId?: string
  clientKey?: string
  onUploaded?: () => void
}

/**
 * The reactive "upload a batch of photos to a note, and if the server says consent is missing,
 * surface the consent form and resume exactly where the batch stopped" behaviour (§44.3, T5-F6) —
 * factored out of `NotePhotoUploader` so it can ALSO drive the "create a note together with staged
 * photos" path in `MasterClientsPage`, where the note (and so its id) doesn't exist until the moment
 * of upload itself. That's the one thing that made the original, component-scoped version
 * (`noteId` as a prop) unusable there: this hook takes `noteId` as an argument to
 * `uploadSequentially` instead, so a caller can create the note first and pass its freshly-minted id
 * in the very same batch of clicks, with no second, parallel implementation of the consent gate.
 *
 * Callers render `consentModal` wherever they want the dialog to appear; the files still waiting to
 * go up live inside this hook's own state (not the caller's), so they survive regardless of whatever
 * the caller does with its own "selected files" state once upload has started.
 */
export function usePhotoUploadWithConsent({ companyId, clientKey, onUploaded }: Options) {
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState('')
  const [awaitingConsent, setAwaitingConsent] = useState<{ noteId: string; files: File[] } | null>(null)

  const uploadMut = useMutation({
    mutationFn: ({ noteId, file }: { noteId: string; file: File }) => clientNotesApi.uploadPhoto(noteId, file),
  })

  const consentMut = useMutation({
    mutationFn: (textVersion: string) => clientConsentsApi.confirmPhotoConsent(companyId!, clientKey!, textVersion),
  })

  const uploadSequentially = async (noteId: string, files: File[]) => {
    if (files.length === 0) return
    setUploading(true)
    setError('')
    for (let i = 0; i < files.length; i++) {
      try {
        await uploadMut.mutateAsync({ noteId, file: files[i] })
        onUploaded?.()
      } catch (e) {
        if (companyId && clientKey && isConsentRequiredError(e)) {
          // The remaining files (this one included) are parked here, not in the caller's state — the
          // caller is free to clear its own "selected files" UI right after this call returns without
          // losing anything the master still needs uploaded.
          setAwaitingConsent({ noteId, files: files.slice(i) })
          setUploading(false)
          return
        }
        setError(getUploadErrorMessage(e))
        setUploading(false)
        return
      }
    }
    setUploading(false)
  }

  const consentModal =
    awaitingConsent && companyId && clientKey ? (
      <ClientConsentModal
        textKey="PhotoConsent"
        title="Согласие на фотофиксацию"
        loading={consentMut.isPending}
        error={consentMut.isError ? 'Не удалось записать согласие. Попробуйте снова.' : undefined}
        onConfirm={(textVersion) => {
          consentMut.mutate(textVersion, {
            onSuccess: () => {
              const pending = awaitingConsent
              setAwaitingConsent(null)
              uploadSequentially(pending.noteId, pending.files)
            },
          })
        }}
        onClose={() => setAwaitingConsent(null)}
      />
    ) : null

  return { uploadSequentially, uploading, error, consentModal }
}
