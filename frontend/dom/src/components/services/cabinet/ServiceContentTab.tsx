// Moved to the shared service screens (ARCHITECTURE_CYCLE42.md §42.12.1); this keeps the old path and renders it in the «Дома» vertical.
import { inVertical } from '@/components/slots/SlotVerticalContext'
import { staysVertical } from '../../../vertical'
import { ServiceContentTab as SharedServiceContentTab } from '@/components/slots/services/cabinet/ServiceContentTab'

export const ServiceContentTab = inVertical(staysVertical, SharedServiceContentTab)
