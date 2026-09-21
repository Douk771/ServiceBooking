import { api } from './client'
import type { DueState, Paged, SubjectRequestDto, SubjectRequestKind, SubjectRequestStatus } from '../types'

export interface SubjectRequestPayload {
  kind: SubjectRequestKind
  phone: string
  contactValue: string
  message: string
  captchaToken?: string
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
}
