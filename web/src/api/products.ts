import { apiClient } from './client'
import type { PagedResult, ProductListDto } from '../types/api'

export interface ProductQuery {
  search?: string
  categoryId?: number
  supplierId?: number
  lowStockOnly?: boolean
  includeInactive?: boolean
  page?: number
  pageSize?: number
}

export async function getProducts(
  query: ProductQuery,
): Promise<PagedResult<ProductListDto>> {
  const params = new URLSearchParams()
  if (query.search) params.set('search', query.search)
  if (query.categoryId != null) params.set('categoryId', String(query.categoryId))
  if (query.supplierId != null) params.set('supplierId', String(query.supplierId))
  if (query.lowStockOnly) params.set('lowStockOnly', 'true')
  if (query.includeInactive) params.set('includeInactive', 'true')
  if (query.page != null) params.set('page', String(query.page))
  if (query.pageSize != null) params.set('pageSize', String(query.pageSize))

  const { data } = await apiClient.get<PagedResult<ProductListDto>>('/products', {
    params,
  })
  return data
}
