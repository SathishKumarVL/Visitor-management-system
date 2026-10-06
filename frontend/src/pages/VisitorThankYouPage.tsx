import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { BrandLogo } from '../components/BrandLogo'
import { Button } from '../components/ui/Button'
import { useAuthStore } from '../store/authStore'
import { useSettingsStore } from '../store/settingsStore'
import { homePathForRoles } from '../lib/utils'
import { useI18n } from '../i18n'

export type VisitorThankYouState = {
  visitorName?: string
  salutation?: string
  visitNumber?: string
}

export function VisitorThankYouPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const { t } = useI18n()
  const notice = (location.state as VisitorThankYouState | null) ?? null
  const user = useAuthStore((s) => s.user)
  const company = useSettingsStore((s) => s.companyName)
  const locationName = user?.locationName

  const place = (locationName || company || t('nav.visitorManagement')).trim()
  const visitorName = notice?.visitorName?.trim()
  const salutation = notice?.salutation?.trim()
  const displayName = [salutation, visitorName].filter(Boolean).join(' ')
  const nextPath = homePathForRoles(user?.roles ?? [], user?.allowedMenuKeys)

  if (!notice) {
    return <Navigate to={nextPath} replace />
  }

  return (
    <div className="mx-auto flex min-h-[70vh] max-w-3xl items-center justify-center px-2 py-6">
      <div className="relative w-full overflow-hidden rounded-[1.75rem] border border-border/70 bg-white p-8 text-center shadow-elevated sm:p-12">
        <div className="pointer-events-none absolute -right-10 -top-10 h-44 w-44 rounded-full bg-aqua-light/80" />
        <div className="pointer-events-none absolute -bottom-16 left-10 h-36 w-36 rounded-full bg-mint" />

        <div className="relative space-y-5">
          <BrandLogo className="mx-auto h-12" />
          <h1 className="text-3xl font-semibold tracking-tight text-ink sm:text-4xl">
            {t('thankYou.headline').replace('{place}', place)}
          </h1>
          {displayName ? (
            <p className="text-lg text-ink-muted">
              {t('thankYou.visitorLine').replace('{name}', displayName)}
            </p>
          ) : null}
          <p className="text-xl font-medium text-primary-deep">{t('thankYou.visitAgain')}</p>
          {notice.visitNumber ? (
            <p className="text-sm text-ink-muted">
              {t('thankYou.visitNumber').replace('{visit}', notice.visitNumber)}
            </p>
          ) : null}

          <div className="pt-6">
            <Button
              className="min-w-[10rem]"
              onClick={() => {
                const roles = user?.roles ?? []
                const canReception = roles.some((r) =>
                  ['SuperAdmin', 'Admin', 'Reception'].includes(r),
                )
                if (canReception) {
                  navigate('/reception', {
                    replace: true,
                    state: {
                      visitorName: notice.visitorName,
                      salutation: notice.salutation,
                      visitNumber: notice.visitNumber,
                      checkedOut: true,
                    },
                  })
                } else {
                  navigate(nextPath, { replace: true })
                }
              }}
            >
              {t('thankYou.ok')}
            </Button>
          </div>
        </div>
      </div>
    </div>
  )
}
