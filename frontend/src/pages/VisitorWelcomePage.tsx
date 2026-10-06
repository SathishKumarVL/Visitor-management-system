import { Fragment, useMemo } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { BrandLogo } from '../components/BrandLogo'
import { WelcomeIllustration } from '../components/WelcomeIllustration'
import { Button } from '../components/ui/Button'
import { useAuthStore } from '../store/authStore'
import { useSettingsStore } from '../store/settingsStore'
import { useI18n } from '../i18n'

export type VisitorWelcomeState = {
  visitorName?: string
  salutation?: string
  pendingApproval?: boolean
  visitNumber?: string
  visitId?: string
  passCode?: string | null
}

export function VisitorWelcomePage() {
  const navigate = useNavigate()
  const location = useLocation()
  const { t } = useI18n()
  const notice = (location.state as VisitorWelcomeState | null) ?? null
  const company = useSettingsStore((s) => s.companyName)
  const locationName = useAuthStore((s) => s.user?.locationName)

  const place = (locationName || company || t('nav.visitorManagement')).trim()
  const visitorName = notice?.visitorName?.trim()
  const salutation = notice?.salutation?.trim()
  const displayName = [salutation, visitorName].filter(Boolean).join(' ')
  const passCode = notice?.passCode?.trim()

  const greeting = useMemo(() => {
    const h = new Date().getHours()
    if (h < 12) return t('greeting.morning')
    if (h < 17) return t('greeting.afternoon')
    return t('greeting.evening')
  }, [t])

  if (!notice?.visitorName) {
    return <Navigate to="/reception" replace />
  }

  return (
    <div className="mx-auto flex min-h-[70vh] max-w-3xl items-center justify-center px-2 py-6">
      <div className="relative w-full overflow-hidden rounded-[1.75rem] border border-border/70 bg-white p-8 text-center shadow-elevated sm:p-12">
        <div className="pointer-events-none absolute -right-10 -top-10 h-44 w-44 rounded-full bg-aqua-light/80" />
        <div className="pointer-events-none absolute -bottom-16 left-10 h-36 w-36 rounded-full bg-mint" />

        <div className="relative space-y-5">
          <BrandLogo className="mx-auto h-12" />
          <WelcomeIllustration className="mx-auto h-36 w-auto sm:h-44" />
          <p className="text-sm font-semibold uppercase tracking-[0.2em] text-primary">{greeting}</p>
          <h1 className="text-3xl font-semibold tracking-tight text-ink sm:text-4xl">
            {t('welcome.headline').replace('{place}', place)}
          </h1>
          {displayName ? (
            <p className="text-lg text-ink-muted">
              {t('welcome.visitorLine')
                .split('{name}')
                .map((part, i) => (
                  <Fragment key={i}>
                    {i > 0 ? <strong className="text-xl font-bold text-ink">{displayName}</strong> : null}
                    {part}
                  </Fragment>
                ))}
            </p>
          ) : null}
          {notice.pendingApproval ? (
            <p className="mx-auto max-w-md text-sm text-amber-800">
              {t('welcome.pendingNote')}
            </p>
          ) : (
            <>
              {passCode ? (
                <div className="mx-auto w-fit rounded-2xl border-2 border-primary/40 bg-primary/5 px-8 py-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.2em] text-primary">
                    {t('welcome.passNumber')}
                  </p>
                  <p className="mt-1 font-mono text-3xl font-bold tracking-wider text-ink sm:text-4xl">{passCode}</p>
                  <p className="mt-2 text-sm text-ink-muted">{t('welcome.passHint').replace('{pass}', passCode)}</p>
                </div>
              ) : null}
              {notice.visitNumber ? (
                <p className="text-sm text-ink-muted">
                  {t('welcome.visitNumber').replace('{visit}', notice.visitNumber)}
                </p>
              ) : null}
            </>
          )}

          <div className="flex flex-wrap items-center justify-center gap-3 pt-6">
            <Button
              className="min-w-[10rem]"
              onClick={() =>
                navigate('/reception', {
                  replace: true,
                  state: {
                    visitorName: notice.visitorName,
                    salutation: notice.salutation,
                    pendingApproval: notice.pendingApproval,
                    visitNumber: notice.visitNumber,
                  },
                })
              }
            >
              {t('welcome.ok')}
            </Button>
            {!notice.pendingApproval ? (
              <Button
                variant="secondary"
                className="min-w-[10rem]"
                onClick={() => navigate('/checkout/face')}
              >
                {t('welcome.checkOut')}
              </Button>
            ) : null}
          </div>
        </div>
      </div>
    </div>
  )
}
