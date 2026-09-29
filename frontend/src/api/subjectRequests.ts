import { api } from './client'
import type { DueState, Paged, SubjectRequestDto, SubjectRequestKind, SubjectRequestStatus } from '../types'

interface SubjectRequestPayload {
  kind: SubjectRequestKind
  phone: string
  contactValue: string
  message: string
  captchaToken?: string
}

/** API_CONTRACT_CYCLE20.md §438 (US-20-09, НЕВЫЙ) — SuperAdmin-only manual registration of a request
 *  that arrived by e-mail or post. `channel` excludes `WebForm` on purpose — that's what the public
 *  form always sends, never something a human picks here. `phone` is optional and normalized server-side. */
export interface ManualSubjectRequestPayload {
  kind: SubjectRequestKind
  channel: 'Email' | 'PostalMail'
  /** ISO date-time the request actually arrived — `dueAt` is computed FROM this, not from "now". */
  receivedAt: string
  phone?: string | null
  contactValue: string
  message: string
}

/**
 * Cycle 5 — data-subject requests without an account (§48). `submit` is anonymous and answers 202
 * with the same shape whether or not the phone number is known to the system (§48.1) — the frontend
 * must not try to infer anything from the response beyond the reference number.
 */
export const subjectRequestsApi = {
  submit: (payload: SubjectRequestPayload) =>
    api.post<{ reference: string; responseDueByWorkingDays: number }>('/subject-requests', payload).then((r) => r.data),

  adminList: (params: { status?: SubjectRequestStatus; kind?: SubjectRequestKind; dueState?: DueState; page?: number; pageSize?: number }) =>
    api.get<Paged<SubjectRequestDto>>('/admin/subject-requests', { params }).then((r) => r.data),

  adminSetStatus: (id: string, status: SubjectRequestStatus, resolution: string) =>
    api.post<void>(`/admin/subject-requests/${id}/status`, { status, resolution }).then((r) => r.data),

  /** POST /api/admin/subject-requests (§438, NEW) — 201 with the same `SubjectRequestDto` shape the
   *  list already renders, so the caller can just prepend/invalidate rather than reshape anything. */
  adminRegister: (payload: ManualSubjectRequestPayload) =>
    api.post<SubjectRequestDto>('/admin/subject-requests', payload).then((r) => r.data),
}
