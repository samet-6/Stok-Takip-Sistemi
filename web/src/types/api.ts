// Manual mirror of the backend API contract.

export type MovementType = 'In' | 'Out'

// --- Auth ---
export interface UserDto {
  id: string
  email: string
  fullName: string
  roles: string[]
}

export interface AuthResponse {
  token: string
  expiresAt: string // ISO 8601
  user: UserDto
}

export interface LoginRequest {
  email: string
  password: string
}

// Self change-password (bank-style): current password required. On success the
// backend re-issues a fresh JWT so the session survives the SecurityStamp bump.
export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

export interface ChangePasswordResponse {
  token: string
  expiresAt: string // ISO 8601
}

// --- Users (Çalışanlar) — admin-managed employee accounts ---
export interface UserListDto {
  id: string
  email: string
  fullName: string
  roles: string[]
  isActive: boolean
  createdAt: string // ISO 8601 → UI "İşe Giriş"
  deactivatedAt?: string | null // ISO 8601 → UI "İşten Çıkış" (only passives)
}

export interface CreateUserRequest {
  fullName: string
  email: string
  password: string
}

export interface UpdateUserRequest {
  fullName: string
  email: string
  password?: string | null // omitted/blank = unchanged
}

export interface UpdateUserStatusRequest {
  isActive: boolean
}

// --- Common ---
export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  /** Stable machine-readable error discriminator (backend RFC 7807 extension). */
  code?: string
  errors?: Record<string, string[]>
}

// --- Categories ---
export interface CategoryDto {
  id: number
  name: string
  description?: string | null
  productCount: number
  createdAt: string
  updatedAt: string
}

export interface CreateCategoryRequest {
  name: string
  description?: string | null
}

export interface UpdateCategoryRequest {
  name: string
  description?: string | null
}

// --- Suppliers ---
export interface SupplierDto {
  id: number
  name: string
  contactEmail: string
  phone?: string | null
  address?: string | null
  isActive: boolean
  productCount: number
  createdAt: string
  updatedAt: string
}

export interface CreateSupplierRequest {
  name: string
  contactEmail: string
  phone?: string | null
  address?: string | null
}

export interface UpdateSupplierRequest {
  name: string
  contactEmail: string
  phone?: string | null
  address?: string | null
  isActive: boolean
}

// --- Products ---

/**
 * Inventory totals aggregated by the database over a scope (search / category / supplier).
 * totalStockValue is current unit price × current stock over ACTIVE products only.
 */
export interface ProductSummaryDto {
  totalProducts: number
  activeCount: number
  passiveCount: number
  lowStockCount: number
  totalStockValue: number
}

export interface ProductListDto {
  id: number
  name: string
  sku: string
  categoryName: string
  supplierName: string
  unitPrice: number
  stockQuantity: number
  minStockLevel: number
  isActive: boolean
  rowVersion: number
}

export interface ProductDetailDto {
  id: number
  name: string
  sku: string
  description?: string | null
  categoryId: number
  categoryName: string
  supplierId: number
  supplierName: string
  unitPrice: number
  stockQuantity: number
  minStockLevel: number
  /** unitPrice × stockQuantity, multiplied by the database. */
  stockValue: number
  isActive: boolean
  rowVersion: number
  createdAt: string
  updatedAt: string
  recentMovements: StockMovementDto[]
}

export interface CreateProductRequest {
  name: string
  sku: string
  description?: string | null
  categoryId: number
  supplierId: number
  unitPrice: number
  minStockLevel: number
  initialStock?: number
}

export interface UpdateProductRequest {
  name: string
  sku: string
  description?: string | null
  categoryId: number
  supplierId: number
  unitPrice: number
  minStockLevel: number
  isActive: boolean
  rowVersion: number
}

// --- Stock Movements ---
export interface StockMovementDto {
  id: number
  productId: number
  productName: string
  type: MovementType
  quantity: number
  note?: string | null
  createdByFullName: string
  createdAt: string // ISO 8601
}

export interface CreateStockMovementRequest {
  productId: number
  type: MovementType
  quantity: number
  note?: string | null
}

export interface StockMovementResponse {
  movement: StockMovementDto
  newStockQuantity: number
}
