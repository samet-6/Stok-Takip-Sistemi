import { apiClient } from './client'
import type {
  CreateStockMovementRequest,
  MovementType,
  PagedResult,
  StockMovementDto,
  StockMovementResponse,
} from '../types/api'

// The backend locks a Çalışan to their own movements regardless of userId; passing
// the caller's own id also constrains an Admin to their own (used by Hesabım).
// supplierId/categoryId narrow by the movement's product; from/to are offset-aware
// ISO instants (see lib/format dayStartIso/dayEndIso) for the date-range filter.
export async function getStockMovements(params: {
  userId?: string
  productId?: number
  supplierId?: number
  categoryId?: number
  type?: MovementType
  from?: string
  to?: string
  page?: number
  pageSize?: number
}): Promise<PagedResult<StockMovementDto>> {
  const { data } = await apiClient.get<PagedResult<StockMovementDto>>(
    '/stock-movements',
    { params },
  )
  return data
}

export async function createStockMovement(
  body: CreateStockMovementRequest,
): Promise<StockMovementResponse> {
  const { data } = await apiClient.post<StockMovementResponse>(
    '/stock-movements',
    body,
  )
  return data
}
