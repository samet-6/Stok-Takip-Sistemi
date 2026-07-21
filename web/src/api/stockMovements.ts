import { apiClient } from './client'
import type {
  CreateStockMovementRequest,
  PagedResult,
  StockMovementDto,
  StockMovementResponse,
} from '../types/api'

// The backend locks a Çalışan to their own movements regardless of userId; passing
// the caller's own id also constrains an Admin to their own (used by Hesabım).
export async function getStockMovements(params: {
  userId?: string
  productId?: number
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
