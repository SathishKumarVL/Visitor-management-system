import { useEffect, useState, type FormEvent } from 'react'
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { BrandLogo } from '../components/BrandLogo'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, PasswordInput } from '../components/ui/Field'
import { Alert } from '../components/ui/Panel'
import { OfflineBanner } from '../components/layout/OfflineBanner'
import { apiErrorMessage } from '../lib/api'
import { useAuthStore } from '../store/authStore'
import { useSettingsStore } from '../store/settingsStore'

export function LoginPage() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const { login, loading, token, user } = useAuthStore()
  const loadSettings = useSettingsStore((s) => s.load)
  const company = useSettingsStore((s) => s.settings.companyName)

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [rememberMe, setRememberMe] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void loadSettings()
  }, [loadSettings])

  if (token && user) {
    return <Navigate to={params.get('redirect') || '/'} replace />
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try {
      const dest = await login(username.trim(), password, rememberMe)
      void loadSettings()
      const redirect = params.get('redirect')
      navigate(dest === '/change-password' ? dest : redirect || dest, { replace: true })
    } catch (err) {
      setError(apiErrorMessage(err, 'Invalid username or password.'))
    }
  }

  return (
    <div className="relative min-h-screen">
      <OfflineBanner />
      <div className="grid min-h-screen lg:grid-cols-2">
        <section className="relative hidden overflow-hidden bg-brand-hero text-white lg:flex lg:flex-col lg:justify-between lg:p-12 xl:p-16">
          <div className="decorative-orb -left-16 -top-10 h-64 w-64" />
          <div className="decorative-orb bottom-20 right-10 h-40 w-40" />
          <div className="decorative-orb right-24 top-1/3 h-24 w-24 opacity-70" />
          <div>
            <BrandLogo className="h-12" light />
            <p className="mt-8 text-sm font-semibold uppercase tracking-[0.2em] text-aqua">
              Visitor Management
            </p>
            <h1 className="mt-4 max-w-md text-4xl font-semibold leading-tight tracking-tight">
              Secure, modern visitor experience for your facility.
            </h1>
            <p className="mt-4 max-w-sm text-sm leading-relaxed text-white/75">
              Reception-ready workflows with check-in, passes, and live on-site visibility — built for tablets and desks.
            </p>
          </div>
          <p className="text-xs text-white/55">Enterprise visitor & facility access platform</p>
        </section>

        <section className="relative flex items-center justify-center bg-page px-4 py-10 sm:px-8">
          <div className="absolute inset-0 bg-brand-soft opacity-70 lg:hidden" />
          <div className="relative w-full max-w-md rounded-3xl border border-border/80 bg-white p-6 shadow-elevated sm:p-8">
            <div className="mb-8 text-center lg:text-left">
              <BrandLogo className="mx-auto h-12 lg:mx-0" />
              <h2 className="mt-5 text-2xl font-semibold text-ink">{company}</h2>
              <p className="mt-1 text-sm text-ink-muted">Sign in to Visitor Management</p>
            </div>

            <form onSubmit={onSubmit} className="space-y-4">
              <div>
                <FieldLabel htmlFor="username">Username</FieldLabel>
                <TextInput
                  id="username"
                  autoComplete="username"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  required
                />
              </div>
              <div>
                <FieldLabel htmlFor="password">Password</FieldLabel>
                <PasswordInput
                  id="password"
                  autoComplete="current-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                />
              </div>
              <label className="flex min-h-11 items-center gap-2 text-sm text-ink-muted">
                <input
                  type="checkbox"
                  checked={rememberMe}
                  onChange={(e) => setRememberMe(e.target.checked)}
                  className="h-4 w-4 accent-primary"
                />
                Remember me on this device
              </label>

              {error ? <Alert tone="error">{error}</Alert> : null}

              <Button type="submit" variant="primary" size="lg" className="w-full" loading={loading}>
                {loading ? 'Signing in…' : 'Sign in'}
              </Button>
            </form>
          </div>
        </section>
      </div>
    </div>
  )
}
