import { Navigate } from 'react-router-dom'

// Old devices page forwards to the profile block (§33.8). Shared by GoodsApp and its test.
export const CABINET_DEVICES_PATH = '/cabinet/devices'
export const cabinetDevicesRedirect = <Navigate to="/profile#devices" replace />
