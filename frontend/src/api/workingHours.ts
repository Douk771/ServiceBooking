import { api } from './client'

export interface WorkingHoursDto {
  id: string
  masterId: string
  companyId: string
  date: string        // 'YYYY-MM-DD'
  startTime: string   // 'HH:mm:ss'
  endTime: string
  isWorking: boolean
  breaks: BreakDto[]
}

export interface BreakDto {
  id: string
  startTime: string
  endTime: string
}

export interface UpsertWorkingHoursPayload {
  masterId: string
  companyId: string
  date: string        // 'YYYY-MM-DD'
  isWorking: boolean
  startTime: string   // 'HH:mm:ss'
  endTime: string
  breaks: { startTime: string; endTime: string }[]
}

export const workingHoursApi = {
  get: (masterId: string, companyId: string, from: string, to: string) =>
    api.get<WorkingHoursDto[]>('/workinghours', { params: { masterId, companyId, from, to } }).then((r) => r.data),
  upsert: (data: UpsertWorkingHoursPayload) =>
    api.put<WorkingHoursDto>('/workinghours', data).then((r) => r.data),
  delete: (id: string) =>
    api.delete(`/workinghours/${id}`),
}
