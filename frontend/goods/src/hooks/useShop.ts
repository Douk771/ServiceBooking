import { useOutletContext } from 'react-router-dom'
import type { ShopManageDto } from '../types'

export interface ShopContext {
  shop: ShopManageDto
  /** Owner or SuperAdmin — the roles that may edit catalog/settings/staff (§392.2). Staff (`Master`) may not. */
  isOwner: boolean
}

export function useShopContext(): ShopContext {
  return useOutletContext<ShopContext>()
}
