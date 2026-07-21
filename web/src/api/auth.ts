import { apiClient } from './client'
import type { AuthResponse, LoginRequest, UserDto } from '../types/api'

export async function login(body: LoginRequest): Promise<AuthResponse> {
  const { data } = await apiClient.post<AuthResponse>('/auth/login', body)
  return data
}

export async function me(): Promise<UserDto> {
  const { data } = await apiClient.get<UserDto>('/auth/me')
  return data
}
