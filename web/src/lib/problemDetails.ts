import axios from 'axios'
import type { ProblemDetails } from '../types/api'

/**
 * Normalizes any thrown error (axios or otherwise) into a ProblemDetails.
 * Backend errors already follow the ProblemDetails shape; everything else
 * gets a generic Turkish fallback message.
 */
export function parseProblemDetails(error: unknown): ProblemDetails {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as ProblemDetails | undefined
    if (data && typeof data === 'object' && ('title' in data || 'detail' in data || 'errors' in data)) {
      return data
    }
    return {
      status: error.response?.status,
      title: 'Bir hata oluştu',
      detail: error.message,
    }
  }
  return { title: 'Beklenmeyen bir hata oluştu' }
}

/** True when the problem carries field-level validation errors (400 model state). */
export function hasFieldErrors(problem: ProblemDetails): boolean {
  return !!problem.errors && Object.keys(problem.errors).length > 0
}

/** Best human-readable message: detail → title → generic fallback. */
export function problemMessage(problem: ProblemDetails): string {
  return problem.detail ?? problem.title ?? 'Bir hata oluştu'
}
