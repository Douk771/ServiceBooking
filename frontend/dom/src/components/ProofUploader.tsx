import type { ComponentProps } from 'react'
import { ProofUploader as SharedProofUploader } from '@/components/slots/ui/ProofUploader'
import { guestBookingsApi } from '../api/guestBookings'

type Props<T extends { paymentProofs: readonly unknown[]; proofs: ComponentProps<typeof SharedProofUploader>['booking']['proofs'] }> = Omit<
  ComponentProps<typeof SharedProofUploader<T>>,
  'upload'
> & {
  /** Sends one file; defaults to the route of a stay booking. A session passes its own route. */
  upload?: ComponentProps<typeof SharedProofUploader<T>>['upload']
}

/** The proof of payment on dom: the shared uploader, a stay booking by default. */
export function ProofUploader<T extends { paymentProofs: readonly unknown[]; proofs: ComponentProps<typeof SharedProofUploader>['booking']['proofs'] }>({ upload, ...rest }: Props<T>) {
  return <SharedProofUploader<T> {...rest} upload={upload ?? ((token, file, onProgress) => guestBookingsApi.uploadProof(token, file, onProgress) as unknown as Promise<T>)} />
}
