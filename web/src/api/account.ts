import { apiClient } from './client'
import type { ChangePasswordRequest, ChangePasswordResponse } from '../types/api'

export async function changePassword(
  body: ChangePasswordRequest,
): Promise<ChangePasswordResponse> {
  const { data } = await apiClient.post<ChangePasswordResponse>(
    '/account/change-password',
    body,
  )
  return data
}
