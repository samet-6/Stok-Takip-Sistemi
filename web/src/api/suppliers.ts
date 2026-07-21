import { apiClient } from './client'
import type {
  SupplierDto,
  CreateSupplierRequest,
  UpdateSupplierRequest,
} from '../types/api'

export async function getSuppliers(): Promise<SupplierDto[]> {
  const { data } = await apiClient.get<SupplierDto[]>('/suppliers')
  return data
}

export async function createSupplier(body: CreateSupplierRequest): Promise<void> {
  await apiClient.post('/suppliers', body)
}

export async function updateSupplier(
  id: number,
  body: UpdateSupplierRequest,
): Promise<void> {
  await apiClient.put(`/suppliers/${id}`, body)
}

export async function deleteSupplier(id: number): Promise<void> {
  await apiClient.delete(`/suppliers/${id}`)
}
