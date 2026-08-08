import { z } from 'zod'

// Form schemas live here rather than next to their pages for two reasons: a page file that
// exports anything besides its component loses Vite Fast Refresh (oxlint's
// react/only-export-components), and the schemas are the one part of a form worth testing on
// its own. Limits mirror the database columns — the backend's DataAnnotations are the source
// of truth and these exist for instant feedback, so the two must not drift.

const isIntString = (v: string) => /^\d+$/.test(v)
const isNumberString = (v: string) => v !== '' && !Number.isNaN(Number(v))

export const productSchema = z.object({
  name: z.string().min(1, 'Bu alan zorunludur').max(150, 'En fazla 150 karakter olabilir'),
  sku: z.string().min(1, 'Bu alan zorunludur').max(30, 'En fazla 30 karakter olabilir'),
  description: z.string().max(500, 'En fazla 500 karakter olabilir'),
  categoryId: z.string().min(1, 'Kategori seçin'),
  supplierId: z.string().min(1, 'Tedarikçi seçin'),
  unitPrice: z
    .string()
    .refine((v) => isNumberString(v) && Number(v) >= 0, 'Geçerli bir sayı girin'),
  minStockLevel: z.string().refine((v) => isIntString(v), 'Geçerli bir tam sayı girin'),
  initialStock: z
    .string()
    .refine((v) => v === '' || (isIntString(v) && Number(v) >= 1), 'En az 1 olmalıdır'),
  isActive: z.boolean(),
})

export type ProductFormValues = z.infer<typeof productSchema>

// Password policy mirror (backend is the source of truth; this is for instant UX).
// On edit the field is optional: empty = unchanged, otherwise must satisfy the policy.
const passwordRules = z
  .string()
  .min(8, 'Şifre en az 8 karakter olmalı')
  .regex(/[a-z]/, 'En az bir küçük harf içermeli')
  .regex(/[A-Z]/, 'En az bir büyük harf içermeli')
  .regex(/\d/, 'En az bir rakam içermeli')
  .regex(/[^A-Za-z0-9]/, 'En az bir özel karakter içermeli')

export function makeUserSchema(isEdit: boolean) {
  return z.object({
    fullName: z
      .string()
      .min(1, 'Bu alan zorunludur')
      .max(100, 'En fazla 100 karakter olabilir'),
    email: z
      .string()
      .min(1, 'Bu alan zorunludur')
      .email('Geçerli bir e-posta girin')
      .max(256, 'En fazla 256 karakter olabilir'),
    password: isEdit ? z.union([z.literal(''), passwordRules]) : passwordRules,
  })
}
