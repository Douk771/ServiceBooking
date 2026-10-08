import { api } from '@/api/client'
import type {
  DateOverrideInput,
  PriceRuleInput,
  PriceRulesDto,
  ScheduleSaveResultDto,
  ServiceItemDto,
  ServiceItemInput,
  ServiceListItemDto,
  ServiceManageDto,
  ServiceMonthDto,
  ServicePhotoDto,
  ServiceSetupInput,
  WeeklyScheduleDto,
  WeeklyScheduleInput,
} from '../types'

const svc = (companyId: string, serviceId?: string) =>
  `/stays/companies/${companyId}/services${serviceId ? `/${serviceId}` : ''}`

/** Cabinet: services of the company (API_CONTRACT_CYCLE39.md §39.26, §39.28). 404 = not a member, 403 = no right. */
export const staysServicesApi = {
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
}
