import { api } from '@/api/client'
import type {
  HouseContentInput,
  HouseListItemDto,
  HouseManageDto,
  HousePhotoDto,
  HousePricingInput,
  HousePublishInput,
  HouseRegistryInput,
  HouseSetupInput,
  PricePeriodDto,
  PricePeriodInput,
} from '../types'

/** Cabinet: houses (API_CONTRACT_CYCLE37.md §37.28). Conflicts of this domain are JSON `StaysConflictDto` (409). */
const base = (companyId: string) => `/stays/companies/${companyId}/houses`

export const staysHousesApi = {
  list: (companyId: string) => api.get<HouseListItemDto[]>(base(companyId)).then((r) => r.data),
  create: (companyId: string, input: { name: string; capacity: number }) =>
    api.post<HouseManageDto>(base(companyId), input).then((r) => r.data),
  order: (companyId: string, ids: string[]) => api.put<HouseListItemDto[]>(`${base(companyId)}/order`, { ids }).then((r) => r.data),
  get: (companyId: string, houseId: string) => api.get<HouseManageDto>(`${base(companyId)}/${houseId}`).then((r) => r.data),
  remove: (companyId: string, houseId: string) => api.delete(`${base(companyId)}/${houseId}`).then(() => undefined),
  updateSetup: (companyId: string, houseId: string, input: HouseSetupInput) =>
    api.put<HouseManageDto>(`${base(companyId)}/${houseId}/setup`, input).then((r) => r.data),
  updateContent: (companyId: string, houseId: string, input: HouseContentInput) =>
    api.put<HouseManageDto>(`${base(companyId)}/${houseId}/content`, input).then((r) => r.data),
  updatePricing: (companyId: string, houseId: string, input: HousePricingInput) =>
    api.put<HouseManageDto>(`${base(companyId)}/${houseId}/pricing`, input).then((r) => r.data),

  pricePeriods: (companyId: string, houseId: string, includePast = false) =>
    api.get<PricePeriodDto[]>(`${base(companyId)}/${houseId}/price-periods`, { params: { includePast } }).then((r) => r.data),
  createPricePeriod: (companyId: string, houseId: string, input: PricePeriodInput) =>
    api.post<PricePeriodDto>(`${base(companyId)}/${houseId}/price-periods`, input).then((r) => r.data),
  updatePricePeriod: (companyId: string, houseId: string, periodId: string, input: PricePeriodInput) =>
    api.put<PricePeriodDto>(`${base(companyId)}/${houseId}/price-periods/${periodId}`, input).then((r) => r.data),
  deletePricePeriod: (companyId: string, houseId: string, periodId: string) =>
    api.delete(`${base(companyId)}/${houseId}/price-periods/${periodId}`).then(() => undefined),

  updateRegistry: (companyId: string, houseId: string, input: HouseRegistryInput) =>
    api.put<HouseManageDto>(`${base(companyId)}/${houseId}/registry`, input).then((r) => r.data),
  publish: (companyId: string, houseId: string, input: HousePublishInput) =>
    api.post<HouseManageDto>(`${base(companyId)}/${houseId}/publish`, input).then((r) => r.data),
  unpublish: (companyId: string, houseId: string) => api.post<HouseManageDto>(`${base(companyId)}/${houseId}/unpublish`, {}).then((r) => r.data),
  archive: (companyId: string, houseId: string) => api.post<HouseManageDto>(`${base(companyId)}/${houseId}/archive`, {}).then((r) => r.data),

  uploadPhoto: (companyId: string, houseId: string, file: File, onProgress?: (percent: number) => void) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<HousePhotoDto>(`${base(companyId)}/${houseId}/photos`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: onProgress
          ? (e) => {
              if (e.total) onProgress(Math.round((e.loaded / e.total) * 100))
            }
          : undefined,
      })
      .then((r) => r.data)
  },
  orderPhotos: (companyId: string, houseId: string, ids: string[]) =>
    api.put<HousePhotoDto[]>(`${base(companyId)}/${houseId}/photos/order`, { ids }).then((r) => r.data),
  deletePhoto: (companyId: string, houseId: string, photoId: string) =>
    api.delete(`${base(companyId)}/${houseId}/photos/${photoId}`).then(() => undefined),
  qr: (companyId: string, houseId: string) =>
    api.get<Blob>(`${base(companyId)}/${houseId}/qr`, { responseType: 'blob' }).then((r) => r.data),
}
