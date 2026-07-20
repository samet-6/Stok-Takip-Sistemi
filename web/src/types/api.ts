// Manual mirror of the backend API contract (docs/api_sozlesmesi.md).
// No codegen — the project is small and the surface is stable.

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

export interface RegisterRequest {
  email: string
  password: string
  fullName: string
}

export interface LoginRequest {
  email: string
  password: string
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
  errors?: Record<string, string[]>
}

// --- Categories ---
export interface CategoryDto {
  id: number
  name: string
  description?: string | null
  productCount: number
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
  contactEmail?: string | null
  phone?: string | null
  productCount: number
}

export interface CreateSupplierRequest {
  name: string
  contactEmail?: string | null
  phone?: string | null
}

export interface UpdateSupplierRequest {
  name: string
  contactEmail?: string | null
  phone?: string | null
}

// --- Products ---
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
  isActive: boolean
  rowVersion: number
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
