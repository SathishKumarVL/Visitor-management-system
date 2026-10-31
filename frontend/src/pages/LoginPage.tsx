import { useEffect, useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { BrandLogo } from '../components/BrandLogo'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, PasswordInput } from '../components/ui/Field'
import { Alert } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { OfflineBanner } from '../components/layout/OfflineBanner'
import { FaceCapture } from '../components/FaceCapture'
import { EcosystemOrbit } from '../components/EcosystemOrbit'
import { apiErrorMessage, authApi } from '../lib/api'
import { useAuthStore } from '../store/authStore'
import { useSettingsStore } from '../store/settingsStore'
import { LanguageSwitcher, useI18n } from '../i18n'

export function LoginPage() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const requested = params.get('redirect')
  // Only pages inside this app; never another site.
  const redirect = requested && requested.startsWith('/') && !requested.startsWith('//') && !requested.startsWith('/login')
    ? requested
    : null
  const { login, faceLogin, loading, token, user } = useAuthStore()
  const loadSettings = useSettingsStore((s) => s.load)

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [rememberMe, setRememberMe] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [faceAvailable, setFaceAvailable] = useState(false)
  const [faceMode, setFaceMode] = useState(false)

  useEffect(() => {
    void loadSettings()
  }, [loadSettings])

  useEffect(() => {
    authApi
      .faceLoginStatus()
      .then((s) => setFaceAvailable(s.available))
      .catch(() => setFaceAvailable(false))
  }, [])

  if (token && user) {
    return <Navigate to={redirect || '/'} replace />
  }

  function goToDestination(dest: string) {
    void loadSettings()
    navigate(dest === '/change-password' ? dest : redirect || dest, { replace: true })
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try {
      goToDestination(await login(username.trim(), password, rememberMe))
    } catch (err) {
      setError(apiErrorMessage(err, t('login.invalidCredentials')))
    }
  }

  async function onFaceCapture(photoBase64: string) {
    setError(null)
    try {
      goToDestination(await faceLogin(photoBase64, rememberMe))
    } catch (err) {
      setError(apiErrorMessage(err, t('login.faceNotRecognised')))
    }
  }

  function switchMode(face: boolean) {
    setError(null)
    setFaceMode(face)
  }

  return (
    <div className="relative min-h-screen">
      {loading ? <LoadingMask label={t('login.signingIn')} /> : null}
      <OfflineBanner />
      <div className="absolute right-4 top-4 z-10">
        <LanguageSwitcher />
      </div>
      <div className="grid min-h-screen lg:grid-cols-2">
        <section className="relative hidden overflow-hidden bg-brand-hero text-white lg:flex lg:flex-col lg:justify-between lg:p-12 xl:p-16">
          <div className="decorative-orb -left-16 -top-10 h-64 w-64" />
          <div className="decorative-orb bottom-20 right-10 h-40 w-40" />
          <div className="decorative-orb right-24 top-1/3 h-24 w-24 opacity-70" />
          <div>
            <BrandLogo className="h-12" light />
            <p className="mt-8 text-sm font-semibold uppercase tracking-[0.2em] text-aqua">
              {t('nav.visitorManagement')}
            </p>
            <h1 className="mt-4 max-w-md text-4xl font-semibold leading-tight tracking-tight">
              {t('login.heroTitle')}
            </h1>
            <p className="mt-4 max-w-sm text-sm leading-relaxed text-white/75">
              {t('login.heroBody')}
            </p>
          </div>
          <div className="relative flex flex-1 items-center justify-center py-6">
            <EcosystemOrbit className="max-h-[calc(100vh-30rem)] min-h-[240px] max-w-[440px]" />
          </div>
          <p className="text-xs text-white/55">{t('login.heroFooter')}</p>
        </section>

        <section className="relative flex items-center justify-center bg-page px-4 py-10 sm:px-8">
          <div className="absolute inset-0 bg-brand-soft opacity-70 lg:hidden" />
          <div className="relative w-full max-w-md rounded-3xl border border-border/80 bg-white p-6 shadow-elevated sm:p-8">
            <div className="mb-8 text-center lg:text-left">
              <BrandLogo className="mx-auto h-14 lg:mx-0" />
              <p className="mt-4 text-sm text-ink-muted">{t('login.title')}</p>
            </div>

            {faceMode ? (
              <div className="space-y-4">
                <p className="text-sm text-ink-muted">{t('login.faceHelp')}</p>
                <FaceCapture
                  onCapture={(photo) => void onFaceCapture(photo)}
                  busy={loading}
                  captureLabel={loading ? t('login.signingIn') : t('login.signInWithFace')}
                />
                <label className="flex min-h-11 items-center gap-2 text-sm text-ink-muted">
                  <input
                    type="checkbox"
                    checked={rememberMe}
                    onChange={(e) => setRememberMe(e.target.checked)}
                    className="h-4 w-4 accent-primary"
                  />
                  {t('login.rememberMe')}
                </label>
                {error ? <Alert tone="error">{error}</Alert> : null}
                <Button type="button" variant="secondary" className="w-full" onClick={() => switchMode(false)}>
                  {t('login.usePassword')}
                </Button>
              </div>
            ) : (
              <form onSubmit={onSubmit} className="space-y-4">
                <div>
                  <FieldLabel htmlFor="username">{t('login.username')}</FieldLabel>
                  <TextInput
                    id="username"
                    autoComplete="username"
                    autoCapitalize="none"
                    autoCorrect="off"
                    spellCheck={false}
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    required
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="password">{t('login.password')}</FieldLabel>
                  <PasswordInput
                    id="password"
                    autoComplete="current-password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    required
                  />
                </div>
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <label className="flex min-h-11 items-center gap-2 text-sm text-ink-muted">
                    <input
                      type="checkbox"
                      checked={rememberMe}
                      onChange={(e) => setRememberMe(e.target.checked)}
                      className="h-4 w-4 accent-primary"
                    />
                    {t('login.rememberMe')}
                  </label>
                  <Link to="/forgot-password" className="min-h-11 py-3 text-sm font-medium text-primary hover:underline">
                    {t('login.forgotPassword')}
                  </Link>
                </div>

                {error ? <Alert tone="error">{error}</Alert> : null}

                <Button type="submit" variant="primary" size="lg" className="w-full" loading={loading}>
                  {loading ? t('login.signingIn') : t('login.signIn')}
                </Button>

                {faceAvailable ? (
                  <>
                    <div className="flex items-center gap-3 text-xs uppercase tracking-wide text-ink-muted">
                      <span className="h-px flex-1 bg-border" />
                      {t('login.or')}
                      <span className="h-px flex-1 bg-border" />
                    </div>
                    <Button type="button" variant="secondary" size="lg" className="w-full" onClick={() => switchMode(true)}>
                      {t('login.signInWithFace')}
                    </Button>
                  </>
                ) : null}
              </form>
            )}
          </div>
        </section>
      </div>
    </div>
  )
}
