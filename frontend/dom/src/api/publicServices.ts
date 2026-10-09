import { api } from '@/api/client'
import type {
  CreateServiceOrderInput,
  CreateServiceOrderResponse,
  PublicServiceDto,
  PublicServiceQuoteInput,
  ServiceAvailabilityDto,
  ServiceQuoteDto,
  ServiceStartsDto,
} from '../types'

/** Режим «к проживанию» — все три параметра вместе (API_CONTRACT_CYCLE39.md §39.22.2). */
export interface StayRangeParams {
  houseId?: string
  checkIn?: string
  checkOut?: string
}

const pub = (serviceId: string) => `/stays/public/services/${serviceId}`

/** Anonymous routes of a service, policy `stays-public` (API_CONTRACT_CYCLE39.md §39.22). */
export const publicServicesApi = {
  page: (companySlug: string, serviceSlug: string) =>
    api
      .get<PublicServiceDto>(`/stays/public/companies/${encodeURIComponent(companySlug)}/services/${encodeURIComponent(serviceSlug)}`)
      .then((r) => r.data),
  availability: (serviceId: string, params: { from?: string; days?: number } & StayRangeParams) =>
    api.get<ServiceAvailabilityDto>(`${pub(serviceId)}/availability`, { params }).then((r) => r.data),
  starts: (serviceId: string, params: { date: string } & StayRangeParams) =>
    api.get<ServiceStartsDto>(`${pub(serviceId)}/starts`, { params }).then((r) => r.data),
  /** Always 200: problems come inside the body (§39.22.4). Nothing is reserved. */
  quote: (serviceId: string, input: PublicServiceQuoteInput) => api.post<ServiceQuoteDto>(`${pub(serviceId)}/quote`, input).then((r) => r.data),
  /** 201 for a new order, 200 for a repeated `idempotencyKey`. */
  createOrder: (serviceId: string, input: CreateServiceOrderInput) =>
    api.post<CreateServiceOrderResponse>(`${pub(serviceId)}/orders`, input).then((r) => r.data),
}
