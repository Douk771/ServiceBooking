import { api } from '@/api/client'
import type {
  CreateStayBookingInput,
  CreateStayBookingResponse,
  HouseAmenityDto,
  HouseCalendarDto,
  PublicHouseDto,
  PublicStaysCompanyDto,
  StayCatalogPage,
  StayQuoteDto,
  StayQuoteInput,
} from '../types'

export interface CatalogQuery {
  checkIn?: string
  checkOut?: string
  guests?: number
  /** The page URL calls it `maxPrice`; the API parameter is `maxPricePerNight` (API_CONTRACT_CYCLE37.md §37.22.2). */
  maxPricePerNight?: number
  page?: number
  pageSize?: number
}

/** Anonymous routes of the vertical, policy `stays-public` (API_CONTRACT_CYCLE37.md §37.22–§37.24). */
export const publicStaysApi = {
  amenities: () => api.get<HouseAmenityDto[]>('/stays/public/amenities').then((r) => r.data),
  catalog: (params: CatalogQuery) => api.get<StayCatalogPage>('/stays/public/catalog', { params }).then((r) => r.data),
  company: (slug: string, params?: { checkIn?: string; checkOut?: string; guests?: number }) =>
    api.get<PublicStaysCompanyDto>(`/stays/public/companies/${encodeURIComponent(slug)}`, { params }).then((r) => r.data),
  house: (companySlug: string, houseSlug: string) =>
    api
      .get<PublicHouseDto>(`/stays/public/companies/${encodeURIComponent(companySlug)}/houses/${encodeURIComponent(houseSlug)}`)
      .then((r) => r.data),
  calendar: (houseId: string, params?: { from?: string; to?: string }) =>
    api.get<HouseCalendarDto>(`/stays/public/houses/${houseId}/calendar`, { params }).then((r) => r.data),
  /** Always 200: problems come inside the body (§37.23). Nothing is reserved. */
  quote: (houseId: string, input: StayQuoteInput) => api.post<StayQuoteDto>(`/stays/public/houses/${houseId}/quote`, input).then((r) => r.data),
  /** 201 for a new booking, 200 for a repeated `idempotencyKey` — the caller gets the same shape either way. */
  createBooking: (houseId: string, input: CreateStayBookingInput) =>
    api.post<CreateStayBookingResponse>(`/stays/public/houses/${houseId}/bookings`, input).then((r) => r.data),
}
