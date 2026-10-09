// Moved to the shared service screens (ARCHITECTURE_CYCLE42.md §42.12.1); this keeps the old path and renders it in the «Дома» vertical.
import { inVertical } from '@/components/slots/SlotVerticalContext'
import { staysVertical } from '../../../vertical'
import { ServiceDayScale as SharedServiceDayScale } from '@/components/slots/services/staff/ServiceDayView'
import { ServiceDayList as SharedServiceDayList } from '@/components/slots/services/staff/ServiceDayView'

export const ServiceDayScale = inVertical(staysVertical, SharedServiceDayScale)
export const ServiceDayList = inVertical(staysVertical, SharedServiceDayList)
