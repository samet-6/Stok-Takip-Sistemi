import { apiClient } from './client'
import type {
  CategoryDto,
  CreateCategoryRequest,
  UpdateCategoryRequest,
} from '../types/api'

export async function getCategories(): Promise<CategoryDto[]> {
  const { data } = await apiClient.get<CategoryDto[]>('/categories')
  return data
}

export async function createCategory(body: CreateCategoryRequest): Promise<void> {
  await apiClient.post('/categories', body)
}

export async function updateCategory(
  id: number,
  body: UpdateCategoryRequest,
): Promise<void> {
  await apiClient.put(`/categories/${id}`, body)
}

export async function deleteCategory(id: number): Promise<void> {
  await apiClient.delete(`/categories/${id}`)
}
