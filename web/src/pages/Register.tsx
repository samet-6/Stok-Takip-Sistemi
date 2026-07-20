import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Link, useNavigate } from 'react-router'
import { Button, Card, Form, Spinner } from 'react-bootstrap'
import { register as registerRequest } from '../api/auth'
import { useAuthStore } from '../stores/authStore'
import { useToast } from '../components/ToastProvider'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'
import { applyServerFieldErrors } from '../lib/formErrors'

const schema = z.object({
  fullName: z.string().min(1, 'Bu alan zorunludur'),
  email: z
    .string()
    .min(1, 'Bu alan zorunludur')
    .email('Geçerli bir e-posta girin'),
  password: z.string().min(1, 'Bu alan zorunludur'),
})

type RegisterForm = z.infer<typeof schema>

export default function Register() {
  const navigate = useNavigate()
  const loginToStore = useAuthStore((s) => s.login)
  const { showError } = useToast()

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<RegisterForm>({ resolver: zodResolver(schema) })

  const onSubmit = async (values: RegisterForm) => {
    try {
      const auth = await registerRequest(values)
      loginToStore(auth) // backend returns a token → automatic sign-in
      navigate('/')
    } catch (err) {
      const problem = parseProblemDetails(err)
      if (problem.status === 409) {
        showError('Bu e-posta adresi zaten kayıtlı.')
      } else if (problem.status === 400 && applyServerFieldErrors(problem, setError, ['fullName', 'email', 'password'])) {
        // field errors surfaced inline
      } else {
        showError(problemMessage(problem))
      }
    }
  }

  return (
    <div className="d-flex justify-content-center">
      <Card style={{ width: '100%', maxWidth: 420 }} className="mt-4 shadow-sm">
        <Card.Body>
          <Card.Title as="h2" className="mb-4 text-center">
            Kayıt Ol
          </Card.Title>
          <Form onSubmit={handleSubmit(onSubmit)} noValidate>
            <Form.Group className="mb-3" controlId="register-fullname">
              <Form.Label>Ad Soyad</Form.Label>
              <Form.Control
                type="text"
                {...register('fullName')}
                isInvalid={!!errors.fullName}
                autoComplete="name"
              />
              <Form.Control.Feedback type="invalid">
                {errors.fullName?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Form.Group className="mb-3" controlId="register-email">
              <Form.Label>E-posta</Form.Label>
              <Form.Control
                type="email"
                {...register('email')}
                isInvalid={!!errors.email}
                autoComplete="email"
              />
              <Form.Control.Feedback type="invalid">
                {errors.email?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Form.Group className="mb-4" controlId="register-password">
              <Form.Label>Şifre</Form.Label>
              <Form.Control
                type="password"
                {...register('password')}
                isInvalid={!!errors.password}
                autoComplete="new-password"
              />
              <Form.Control.Feedback type="invalid">
                {errors.password?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Button type="submit" className="w-100" disabled={isSubmitting}>
              {isSubmitting ? (
                <>
                  <Spinner as="span" size="sm" animation="border" className="me-2" />
                  Kaydediliyor…
                </>
              ) : (
                'Kayıt Ol'
              )}
            </Button>
          </Form>
          <div className="text-center mt-3">
            Zaten hesabınız var mı? <Link to="/login">Giriş yapın</Link>
          </div>
        </Card.Body>
      </Card>
    </div>
  )
}
