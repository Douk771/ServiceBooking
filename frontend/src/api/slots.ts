import { api } from '@/api/client'
import { httpStatus } from '@/utils/slots/slotError'
import type {
  CreateServiceOrderInput,
  CreateServiceOrderResponse,
  DateOverrideInput,
  ManualServiceOrderInput,
  PriceRuleInput,
  PriceRulesDto,
  PublicServiceDto,
  PublicServiceOrderDto,
  PublicServiceQuoteInput,
  PushSubscriptionInput,
  ScheduleSaveResultDto,
  ServiceAvailabilityDto,
  ServiceDayDto,
  ServiceItemDto,
  ServiceItemInput,
  ServiceItemPublicDto,
  ServiceListItemDto,
  ServiceManageDto,
  ServiceMonthDto,
  ServicePhotoDto,
  ServiceQuoteDto,
  ServiceSetupInput,
  ServiceStartsDto,
  StaffServiceQuoteInput,
  StaffServiceSessionCardDto,
  StaffServiceSessionPage,
  WeeklyScheduleDto,
  WeeklyScheduleInput,
} from '@/types/slots'

/** Режим «к проживанию» — все три параметра вместе (API_CONTRACT_CYCLE39.md §39.22.2). */
export interface StayRangeParams {
  houseId?: string
  checkIn?: string
  checkOut?: string
}

export interface SessionListQuery {
  status?: string
  serviceId?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

/** The routes of a vertical's services: `prefix` is the vertical's API root (ARCHITECTURE_CYCLE42.md §42.12.1). */
export type SlotApiPrefix = '/stays' | '/baths'

/** One factory for every vertical: dom builds it with '/stays', bani with '/baths'. */
export function createSlotApi(prefix: SlotApiPrefix) {
  const pub = (serviceId: string) => `${prefix}/public/services/${serviceId}`
  const base = (token: string) => `${prefix}/service-orders/public/${encodeURIComponent(token)}`
  const svc = (companyId: string, serviceId?: string) => `${prefix}/companies/${companyId}/services${serviceId ? `/${serviceId}` : ''}`
  const co = (companyId: string) => `${prefix}/companies/${companyId}`

  return {
    /** Anonymous routes of a service (API_CONTRACT_CYCLE39.md §39.22). */
    publicServices: {

  page: (companySlug: string, serviceSlug: string) =>
    api
      .get<PublicServiceDto>(`${prefix}/public/companies/${encodeURIComponent(companySlug)}/services/${encodeURIComponent(serviceSlug)}`)
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

    },
    /** A separate session by its token (API_CONTRACT_CYCLE39.md §39.23). Unknown token → 404, empty body. */
    orders: {

  get: (token: string) => api.get<PublicServiceOrderDto>(base(token)).then((r) => r.data),

  uploadProof: (token: string, file: File, onProgress?: (percent: number) => void) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<PublicServiceOrderDto>(`${base(token)}/payment-proofs`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: onProgress
          ? (e) => {
              if (e.total) onProgress(Math.round((e.loaded / e.total) * 100))
            }
          : undefined,
      })
      .then((r) => r.data)
  },
  proofBlob: (token: string, proofId: string) =>
    api.get<Blob>(`${base(token)}/payment-proofs/${proofId}`, { responseType: 'blob' }).then((r) => r.data),
  cancel: (token: string) => api.post<PublicServiceOrderDto>(`${base(token)}/cancel`, {}).then((r) => r.data),
  pushSubscribe: (token: string, input: PushSubscriptionInput) => api.post(`${base(token)}/push-subscription`, input).then(() => undefined),
  pushUnsubscribe: (token: string, endpoint: string) => api.post(`${base(token)}/push-subscription/remove`, { endpoint }).then(() => undefined),

    },
    /** Cabinet: services of the company (API_CONTRACT_CYCLE39.md §39.26, §39.28). 404 = not a member, 403 = no right. */
    cabinet: {

  list: (companyId: string) => api.get<ServiceListItemDto[]>(svc(companyId)).then((r) => r.data),
  create: (companyId: string, name: string) => api.post<ServiceManageDto>(svc(companyId), { name }).then((r) => r.data),
  reorder: (companyId: string, ids: string[]) => api.put<ServiceListItemDto[]>(`${svc(companyId)}/order`, { ids }).then((r) => r.data),
  get: (companyId: string, serviceId: string) => api.get<ServiceManageDto>(svc(companyId, serviceId)).then((r) => r.data),
  remove: (companyId: string, serviceId: string) => api.delete(svc(companyId, serviceId)).then(() => undefined),
  setup: (companyId: string, serviceId: string, input: ServiceSetupInput) =>
    api.put<ServiceManageDto>(`${svc(companyId, serviceId)}/setup`, input).then((r) => r.data),
  content: (companyId: string, serviceId: string, description: string | null) =>
    api.put<ServiceManageDto>(`${svc(companyId, serviceId)}/content`, { description }).then((r) => r.data),
  publish: (companyId: string, serviceId: string) => api.post<ServiceManageDto>(`${svc(companyId, serviceId)}/publish`, {}).then((r) => r.data),
  unpublish: (companyId: string, serviceId: string) => api.post<ServiceManageDto>(`${svc(companyId, serviceId)}/unpublish`, {}).then((r) => r.data),
  archive: (companyId: string, serviceId: string) => api.post<ServiceManageDto>(`${svc(companyId, serviceId)}/archive`, {}).then((r) => r.data),

  uploadPhoto: (companyId: string, serviceId: string, file: File, onProgress?: (percent: number) => void) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<ServicePhotoDto>(`${svc(companyId, serviceId)}/photos`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: onProgress
          ? (e) => {
              if (e.total) onProgress(Math.round((e.loaded / e.total) * 100))
            }
          : undefined,
      })
      .then((r) => r.data)
  },
  reorderPhotos: (companyId: string, serviceId: string, ids: string[]) =>
    api.put<ServicePhotoDto[]>(`${svc(companyId, serviceId)}/photos/order`, { ids }).then((r) => r.data),
  deletePhoto: (companyId: string, serviceId: string, photoId: string) =>
    api.delete(`${svc(companyId, serviceId)}/photos/${photoId}`).then(() => undefined),

  priceRules: (companyId: string, serviceId: string) => api.get<PriceRulesDto>(`${svc(companyId, serviceId)}/price-rules`).then((r) => r.data),
  addPriceRule: (companyId: string, serviceId: string, input: PriceRuleInput) =>
    api.post<PriceRulesDto>(`${svc(companyId, serviceId)}/price-rules`, input).then((r) => r.data),
  updatePriceRule: (companyId: string, serviceId: string, ruleId: string, input: PriceRuleInput) =>
    api.put<PriceRulesDto>(`${svc(companyId, serviceId)}/price-rules/${ruleId}`, input).then((r) => r.data),
  deletePriceRule: (companyId: string, serviceId: string, ruleId: string) =>
    api.delete<PriceRulesDto>(`${svc(companyId, serviceId)}/price-rules/${ruleId}`).then((r) => r.data),

  items: (companyId: string, serviceId: string) => api.get<ServiceItemDto[]>(`${svc(companyId, serviceId)}/items`).then((r) => r.data),
  addItem: (companyId: string, serviceId: string, input: ServiceItemInput) =>
    api.post<ServiceItemDto>(`${svc(companyId, serviceId)}/items`, input).then((r) => r.data),
  reorderItems: (companyId: string, serviceId: string, ids: string[]) =>
    api.put<ServiceItemDto[]>(`${svc(companyId, serviceId)}/items/order`, { ids }).then((r) => r.data),
  updateItem: (companyId: string, serviceId: string, itemId: string, input: ServiceItemInput) =>
    api.put<ServiceItemDto>(`${svc(companyId, serviceId)}/items/${itemId}`, input).then((r) => r.data),
  deleteItem: (companyId: string, serviceId: string, itemId: string) =>
    api.delete(`${svc(companyId, serviceId)}/items/${itemId}`).then(() => undefined),

  weekly: (companyId: string, serviceId: string) => api.get<WeeklyScheduleDto>(`${svc(companyId, serviceId)}/weekly-schedule`).then((r) => r.data),
  saveWeekly: (companyId: string, serviceId: string, input: WeeklyScheduleInput) =>
    api.put<ScheduleSaveResultDto>(`${svc(companyId, serviceId)}/weekly-schedule`, input).then((r) => r.data),
  month: (companyId: string, serviceId: string, month: string) =>
    api.get<ServiceMonthDto>(`${svc(companyId, serviceId)}/date-overrides`, { params: { month } }).then((r) => r.data),
  saveDate: (companyId: string, serviceId: string, date: string, input: DateOverrideInput) =>
    api.put<ScheduleSaveResultDto>(`${svc(companyId, serviceId)}/date-overrides/${date}`, input).then((r) => r.data),
  resetDate: (companyId: string, serviceId: string, date: string) =>
    api.delete<ScheduleSaveResultDto>(`${svc(companyId, serviceId)}/date-overrides/${date}`).then((r) => r.data),

  /**
   * Positions for the staff picker: the owner's route. A manager without `ManageServices` gets 403 and simply has no positions to add
   * (the contract has no manager-readable source of the positions — raised in the report of cycle 39).
   */
  staffPickItems: async (companyId: string, serviceId: string): Promise<ServiceItemPublicDto[]> => {
    try {
      const items = await api.get<ServiceItemDto[]>(`${svc(companyId, serviceId)}/items`).then((r) => r.data)
      return items.filter((i) => i.isActive).map((i) => ({ id: i.id, name: i.name, priceRub: i.priceRub, maxPerSession: i.maxPerSession }))
    } catch (err) {
      if (httpStatus(err) === 403) return []
      throw err
    }
  },

    },
    /** «День услуг» and sessions of the cabinet (API_CONTRACT_CYCLE39.md §39.29, §39.30). */
    sessions: {
  serviceDay: (companyId: string, date: string) => api.get<ServiceDayDto>(`${co(companyId)}/service-day`, { params: { date } }).then((r) => r.data),
  sessions: (companyId: string, q: SessionListQuery) =>
    api.get<StaffServiceSessionPage>(`${co(companyId)}/service-sessions`, { params: q }).then((r) => r.data),
  session: (companyId: string, sessionId: string) =>
    api.get<StaffServiceSessionCardDto>(`${co(companyId)}/service-sessions/${sessionId}`).then((r) => r.data),
  confirmSessionPayment: (companyId: string, sessionId: string, expectedVersion: number) =>
    api.post<StaffServiceSessionCardDto>(`${co(companyId)}/service-sessions/${sessionId}/confirm-payment`, { expectedVersion }).then((r) => r.data),
  rejectSessionPayment: (companyId: string, sessionId: string, expectedVersion: number, reason: string) =>
    api
      .post<StaffServiceSessionCardDto>(`${co(companyId)}/service-sessions/${sessionId}/reject-payment`, { expectedVersion, reason })
      .then((r) => r.data),
  cancelSession: (companyId: string, sessionId: string, expectedVersion: number, reason: string) =>
    api.post<StaffServiceSessionCardDto>(`${co(companyId)}/service-sessions/${sessionId}/cancel`, { expectedVersion, reason }).then((r) => r.data),
  sessionProofBlob: (companyId: string, sessionId: string, proofId: string) =>
    api.get<Blob>(`${co(companyId)}/service-sessions/${sessionId}/payment-proofs/${proofId}`, { responseType: 'blob' }).then((r) => r.data),
  staffStarts: (companyId: string, serviceId: string, params: { date: string; bookingId?: string }) =>
    api.get<ServiceStartsDto>(`${co(companyId)}/services/${serviceId}/starts`, { params }).then((r) => r.data),
  sessionQuote: (companyId: string, input: StaffServiceQuoteInput) =>
    api.post<ServiceQuoteDto>(`${co(companyId)}/service-sessions/quote`, input).then((r) => r.data),
  createManualOrder: (companyId: string, input: ManualServiceOrderInput) =>
    api.post<StaffServiceSessionCardDto>(`${co(companyId)}/service-sessions`, input).then((r) => r.data),
    },
  }
}

export type SlotApi = ReturnType<typeof createSlotApi>
