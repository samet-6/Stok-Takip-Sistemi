import { apiClient } from './client'
import type { NotificationListResponse } from '../types/api'

// Admin-only endpoints — a Çalışan gets 403, which is why the bell is not rendered for them.
export async function getNotifications(params: {
  page?: number
  pageSize?: number
}): Promise<NotificationListResponse> {
  const { data } = await apiClient.get<NotificationListResponse>('/notifications', { params })
  return data
}

export async function markNotificationRead(id: number): Promise<void> {
  await apiClient.post(`/notifications/${id}/read`)
}

export async function markAllNotificationsRead(): Promise<void> {
  await apiClient.post('/notifications/read-all')
}
