import { api } from './client'

export interface WorkingHoursDto {
  id: string
  masterId: string
  companyId: string
  dayOfWeek: number
  startTime: string
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
  dayOfWeek: number
  isWorking: boolean
  startTime: string
  endTime: string
  breaks: { startTime: string; endTime: string }[]
}

export const workingHoursApi = {
  get: (masterId: string, companyId: string) =>
    api.get<WorkingHoursDto[]>('/workinghours', { params: { masterId, companyId } }).then((r) => r.data),
  upsert: (data: UpsertWorkingHoursPayload) =>
    api.put<WorkingHoursDto>('/workinghours', data).then((r) => r.data),
}
