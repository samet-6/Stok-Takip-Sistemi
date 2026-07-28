import { apiClient } from './client'
import type {
  AuthResponse,
  HubTicketResponse,
  LoginRequest,
  UserDto,
} from '../types/api'

export async function login(body: LoginRequest): Promise<AuthResponse> {
  const { data } = await apiClient.post<AuthResponse>('/auth/login', body)
  return data
}

export async function me(): Promise<UserDto> {
  const { data } = await apiClient.get<UserDto>('/auth/me')
  return data
}

// Called before every hub (re)connection attempt. Goes through apiClient on purpose:
// the request carries the session token and a 401 here means the session is gone, which
// the shared interceptor already turns into logout + redirect.
export async function hubTicket(): Promise<HubTicketResponse> {
  const { data } = await apiClient.post<HubTicketResponse>('/auth/hub-ticket')
  return data
}
