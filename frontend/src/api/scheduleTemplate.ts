import { api } from './client'

export interface DayTemplate {
  dayOfWeek: number // 1=Пн, 7=Вс
  isWorking: boolean
  startTime: string // HH:mm
  endTime: string   // HH:mm
}

export interface WeeklyTemplate {
  masterId: string
  companyId: string
  days: DayTemplate[]
}

export const scheduleTemplateApi = {
  get: (masterId: string, companyId: string) =>
    api.get<DayTemplate[]>('/schedule-template', { params: { masterId, companyId } }).then(r => r.data),
  save: (data: WeeklyTemplate) =>
    api.put('/schedule-template', data),
  apply: (masterId: string, companyId: string, from: string, to: string) =>
    api.post('/schedule-template/apply', null, { params: { masterId, companyId, from, to } }),
}
