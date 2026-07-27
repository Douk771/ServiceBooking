import { api } from './client'

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
  getForCompany: (companyId: string) =>
    api.get<Review[]>(`/companies/${companyId}/reviews`).then(r => r.data),

  canReview: () =>
    api.get<CanReviewItem[]>('/reviews/can-review').then(r => r.data),

  submit: (bookingId: string, rating: number, comment: string) =>
    api.post('/reviews', { bookingId, rating, comment }),
}
