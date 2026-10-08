import { StaffMaxCard as SharedStaffMaxCard } from '@/components/staffMax/StaffMaxCard'
import { getGoodsErrorMessage } from '../../utils/orderError'
import { ErrorState, InlineError, LoadingList } from '../StatePanels'

/**
 * «Заказы в MAX» of goods. The card itself lives in `src/components/staffMax/` since cycle 39 (ARCHITECTURE_CYCLE39.md §39.15.1): dom shows the same
 * card. Goods passes its own error mapping and state panels, so nothing on its screens changed.
 */
export function StaffMaxCard() {
  return <SharedStaffMaxCard getErrorMessage={getGoodsErrorMessage} slots={{ LoadingList, ErrorState, InlineError }} />
}
