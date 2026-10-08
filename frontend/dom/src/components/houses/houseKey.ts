/** react-query key of one house in the cabinet (the whole `HouseManageDto`). */
export const houseKey = (companyId: string, houseId: string) => ['stays-house-manage', companyId, houseId] as const
