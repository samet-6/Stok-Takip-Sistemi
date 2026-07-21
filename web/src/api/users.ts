import { apiClient } from './client'
import type {
  UserListDto,
  CreateUserRequest,
  UpdateUserRequest,
  UpdateUserStatusRequest,
} from '../types/api'

export async function getUsers(): Promise<UserListDto[]> {
  const { data } = await apiClient.get<UserListDto[]>('/users')
  return data
}

export async function createUser(body: CreateUserRequest): Promise<UserListDto> {
  const { data } = await apiClient.post<UserListDto>('/users', body)
  return data
}

export async function updateUser(id: string, body: UpdateUserRequest): Promise<void> {
  await apiClient.put(`/users/${id}`, body)
}

export async function setUserStatus(
  id: string,
  body: UpdateUserStatusRequest,
): Promise<void> {
  await apiClient.patch(`/users/${id}`, body)
}
