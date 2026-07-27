import { apiClient } from './client'
import type {
  PagedResult,
  ProductListDto,
  ProductDetailDto,
  ProductSummaryDto,
  CreateProductRequest,
  UpdateProductRequest,
} from '../types/api'

/** The catalog dimensions a request is about. */
export interface ProductScope {
  categoryId?: number
  supplierId?: number
}

export interface ProductQuery extends ProductScope {
  search?: string
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

/**
 * Inventory totals for a scope, counted and summed by the database — exact no matter
 * how many products match, unlike aggregating a fetched page in the browser.
 */
export async function getProductSummary(scope: ProductScope): Promise<ProductSummaryDto> {
  const params = new URLSearchParams()
  if (scope.categoryId != null) params.set('categoryId', String(scope.categoryId))
  if (scope.supplierId != null) params.set('supplierId', String(scope.supplierId))

  const { data } = await apiClient.get<ProductSummaryDto>('/products/summary', { params })
  return data
}

export async function getProduct(id: number): Promise<ProductDetailDto> {
  const { data } = await apiClient.get<ProductDetailDto>(`/products/${id}`)
  return data
}

export async function createProduct(body: CreateProductRequest): Promise<void> {
  await apiClient.post('/products', body)
}

export async function updateProduct(
  id: number,
  body: UpdateProductRequest,
): Promise<void> {
  await apiClient.put(`/products/${id}`, body)
}

/**
 * Deletes a product. Returns 'hard' when the row was removed (204) or 'soft'
 * when it was deactivated because movements exist (200) — the caller shows a
 * different toast per outcome.
 */
export async function deleteProduct(id: number): Promise<'hard' | 'soft'> {
  const res = await apiClient.delete(`/products/${id}`)
  return res.status === 200 ? 'soft' : 'hard'
}
