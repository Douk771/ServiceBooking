import { api } from './client'
import type { Paged } from '../types'

export interface Review {
  rating: number
  comment: string | null
  reviewerName: string
  masterName: string
  serviceName: string
  createdAt: string
}

export interface CanReviewItem {
  bookingId: string
  serviceName: string
  masterName: string
  date: string
}

export const reviewsApi = {
  // API_CONTRACT.md §11.2 (BREAKING) — array replaced by the Paged<T> envelope, sorted createdAt DESC.
  getForCompany: (companyId: string, page = 1, pageSize = 20) =>
    api.get<Paged<Review>>(`/companies/${companyId}/reviews`, { params: { page, pageSize } }).then((r) => r.data),

  canReview: () => api.get<CanReviewItem[]>('/reviews/can-review').then((r) => r.data),

  submit: (bookingId: string, rating: number, comment: string) => api.post('/reviews', { bookingId, rating, comment }),
}
