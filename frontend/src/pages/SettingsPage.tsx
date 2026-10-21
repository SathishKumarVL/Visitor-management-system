import { useEffect, useRef, useState, type FormEvent } from 'react'
import { apiErrorMessage, mastersApi, settingsApi } from '../lib/api'
import type { MasterItemDto, PassNumberSeriesDto, SettingsDto } from '../types/api'
import { Alert, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, PasswordInput, TextSelect } from '../components/ui/Field'
import { useSettingsStore } from '../store/settingsStore'
import { BrandLogo } from '../components/BrandLogo'
import { assetUrl, cn, hasAnyRole } from '../lib/utils'
import { FONT_PRESETS, FONT_SIZES, THEME_PRESETS, applyBranding } from '../lib/branding'
import { useI18n } from '../i18n'
import { useAuthStore } from '../store/authStore'
import { ApiKeysPanel } from '../components/ApiKeysPanel'
import { LookupSourcesPanel } from '../components/LookupSourcesPanel'
import { MailContentPanel } from './MailContentPage'
import { useSearchParams } from 'react-router-dom'

const SECTIONS = [
  { id: 'general', labelKey: 'settings.general' },
  { id: 'branding', labelKey: 'settings.branding' },
  { id: 'visitor', labelKey: 'settings.visitorRules' },
  { id: 'pass', labelKey: 'settings.passSettings' },
  { id: 'email', labelKey: 'settings.email' },
  { id: 'mailContent', labelKey: 'settings.mailContent' },
  { id: 'security', labelKey: 'settings.security' },
  { id: 'apiKeys', labelKey: 'settings.apiKeys', superAdminOnly: true },
  { id: 'lookups', labelKey: 'settings.lookupSources', superAdminOnly: true },
] as const

/** Stored once for the whole company, so only a Super Admin can change them. */
const COMPANY_WIDE_SECTIONS: readonly string[] = ['general', 'visitor', 'security']

export function SettingsPage() {
  const { t } = useI18n()
  const isSuperAdmin = hasAnyRole(useAuthStore((s) => s.user?.roles) ?? [], ['SuperAdmin'])
  const userLocationId = useAuthStore((s) => s.user?.locationId) ?? ''
  const locationBound = !isSuperAdmin && !!userLocationId
  const visibleSections = SECTIONS.filter(
    (s) =>
      (!('superAdminOnly' in s) || isSuperAdmin) &&
      (!locationBound || !COMPANY_WIDE_SECTIONS.includes(s.id)),
  )
  const { settings, load, update, uploadLogo, loaded } = useSettingsStore()
  const [form, setForm] = useState<SettingsDto>(settings)
  const [smtpPassword, setSmtpPassword] = useState('')
  const [testEmailTo, setTestEmailTo] = useState('')
  const [testSendAs, setTestSendAs] = useState('')
  const [testingEmail, setTestingEmail] = useState(false)
  const [smtpLocationId, setSmtpLocationId] = useState(locationBound ? userLocationId : '')
  const [passLocationId, setPassLocationId] = useState(locationBound ? userLocationId : '')
  const [locations, setLocations] = useState<MasterItemDto[]>([])
  const [loadingSmtp, setLoadingSmtp] = useState(false)
  const [saving, setSaving] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [searchParams] = useSearchParams()
  const [section, setSection] = useState<(typeof SECTIONS)[number]['id']>(
    () => visibleSections.find((s) => s.id === searchParams.get('tab'))?.id ?? 'branding',
  )
  const fileRef = useRef<HTMLInputElement>(null)

  const [series, setSeries] = useState<PassNumberSeriesDto | null>(null)
  const [seriesForm, setSeriesForm] = useState({
    prefix: 'VMS-',
    year: '' as string,
    startNumber: 1000,
    endNumber: 999999,
    currentNumber: 999,
    isActive: true,
  })
  const [passSaving, setPassSaving] = useState(false)

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    void mastersApi.locations(true).then(setLocations).catch(() => setLocations([]))
  }, [])

  useEffect(() => {
    if (smtpLocationId) return
    setForm(settings)
    setSmtpPassword('')
  }, [settings, smtpLocationId])

  useEffect(() => {
    if (!smtpLocationId) return
    let cancelled = false
    void (async () => {
      setLoadingSmtp(true)
      setError(null)
      try {
        const locSettings = await settingsApi.get(smtpLocationId)
        if (cancelled) return
        setForm((prev) => ({
          ...prev,
          companyName: locSettings.companyName,
          logoPath: locSettings.logoPath,
          themePreset: locSettings.themePreset,
          fontPreset: locSettings.fontPreset,
          fontSize: locSettings.fontSize,
          smtpEnabled: locSettings.smtpEnabled,
          smtpHost: locSettings.smtpHost,
          smtpPort: locSettings.smtpPort,
          smtpEnableSsl: locSettings.smtpEnableSsl,
          smtpUsername: locSettings.smtpUsername,
          smtpFromAddress: locSettings.smtpFromAddress,
          smtpFromName: locSettings.smtpFromName,
          smtpIgnoreSslErrors: locSettings.smtpIgnoreSslErrors,
          smtpPasswordConfigured: locSettings.smtpPasswordConfigured,
        }))
        applyBranding(locSettings.themePreset, locSettings.fontPreset, locSettings.fontSize)
        setSmtpPassword('')
      } catch (err) {
        if (!cancelled) setError(apiErrorMessage(err, 'Unable to load location settings.'))
      } finally {
        if (!cancelled) setLoadingSmtp(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [smtpLocationId])

  useEffect(() => {
    if (section !== 'pass') return
    void (async () => {
      try {
        const s = await settingsApi.getPassSeries(passLocationId || undefined)
        setSeries(s)
        if (s) {
          setSeriesForm({
            prefix: s.prefix,
            year: s.year != null ? String(s.year) : '',
            startNumber: s.startNumber,
            endNumber: s.endNumber,
            currentNumber: s.currentNumber,
            isActive: s.isActive,
          })
        } else {
          setSeriesForm({
            prefix: 'VMS-',
            year: '',
            startNumber: 1000,
            endNumber: 999999,
            currentNumber: 999,
            isActive: true,
          })
        }
      } catch (err) {
        setError(apiErrorMessage(err, 'Unable to load pass settings.'))
      }
    })()
  }, [section, passLocationId])

  function patchForm(partial: Partial<SettingsDto>) {
    const next = { ...form, ...partial }
    setForm(next)
    if (
      partial.themePreset !== undefined ||
      partial.fontPreset !== undefined ||
      partial.fontSize !== undefined
    ) {
      applyBranding(next.themePreset, next.fontPreset, next.fontSize)
    }
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError(null)
    setMessage(null)
    try {
      const saved = await update(
        {
          ...form,
          approvalRequired: false,
          walkInApprovalRequired: false,
          smtpPassword: smtpPassword.trim() || null,
        },
        smtpLocationId || undefined,
      )
      setSmtpPassword('')
      if (smtpLocationId) {
        setForm((prev) => ({
          ...prev,
          ...saved,
          smtpPasswordConfigured: saved.smtpPasswordConfigured,
        }))
      }
      setMessage(t('settings.saved'))
    } catch (err) {
      setError(apiErrorMessage(err, t('settings.saveError')))
    } finally {
      setSaving(false)
    }
  }

  async function onLogoSelected(file: File | null) {
    if (!file) return
    setUploading(true)
    setError(null)
    setMessage(null)
    try {
      const next = await uploadLogo(file, smtpLocationId || undefined)
      setForm((prev) => ({
        ...prev,
        logoPath: next.logoPath,
        companyName: next.companyName,
        themePreset: next.themePreset,
        fontPreset: next.fontPreset,
        fontSize: next.fontSize,
      }))
      setMessage('Logo updated.')
    } catch (err) {
      setError(apiErrorMessage(err, 'Unable to upload logo.'))
    } finally {
      setUploading(false)
      if (fileRef.current) fileRef.current.value = ''
    }
  }

  async function sendTestEmail() {
    if (!testEmailTo.trim()) {
      setError(t('settings.testEmailError'))
      return
    }
    setTestingEmail(true)
    setError(null)
    setMessage(null)
    try {
      await update(
        {
          ...form,
          approvalRequired: false,
          walkInApprovalRequired: false,
          smtpPassword: smtpPassword.trim() || null,
        },
        smtpLocationId || undefined,
      )
      if (smtpPassword.trim()) setSmtpPassword('')
      const sent = await settingsApi.sendTestEmail(
        testEmailTo.trim(),
        smtpLocationId || undefined,
        testSendAs.trim() || undefined,
      )
      setMessage(sent || t('settings.testEmailSent'))
    } catch (err) {
      setError(apiErrorMessage(err, t('settings.testEmailError')))
    } finally {
      setTestingEmail(false)
    }
  }

  async function saveSeries() {
    setPassSaving(true)
    setError(null)
    setMessage(null)
    try {
      const saved = await settingsApi.upsertPassSeries({
        locationId: passLocationId || null,
        prefix: seriesForm.prefix,
        year: seriesForm.year.trim() ? Number(seriesForm.year) : null,
        startNumber: seriesForm.startNumber,
        endNumber: seriesForm.endNumber,
        currentNumber: seriesForm.currentNumber,
        isActive: seriesForm.isActive,
      })
      setSeries(saved)
      setMessage(t('settings.saved'))
    } catch (err) {
      setError(apiErrorMessage(err, t('settings.saveError')))
    } finally {
      setPassSaving(false)
    }
  }

  if (!loaded) return <Spinner label="Loading settings…" />

  return (
    <div>
      {saving ? <LoadingMask label="Saving settings…" /> : null}
      {uploading ? <LoadingMask label="Uploading logo…" /> : null}
      {passSaving ? <LoadingMask label="Saving pass numbers…" /> : null}
      {testingEmail ? <LoadingMask label="Sending test email…" /> : null}
      <PageHeader
        eyebrow={t('settings.eyebrow')}
        title={t('settings.title')}
        subtitle={t('settings.subtitle')}
      />
      {error ? (
        <div className="mb-4">
          <Alert tone="error">{error}</Alert>
        </div>
      ) : null}
      {message ? (
        <div className="mb-4">
          <Alert tone="success">{message}</Alert>
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[220px_1fr_240px]">
        <Panel className="h-fit p-2">
          <nav className="flex flex-col gap-1" aria-label="Settings categories">
            {visibleSections.map((s) => (
              <button
                key={s.id}
                type="button"
                className={cn(
                  'rounded-lg px-3 py-2 text-left text-sm font-medium',
                  section === s.id ? 'bg-brand-gradient text-white' : 'text-ink-muted hover:bg-aqua-light',
                )}
                onClick={() => setSection(s.id)}
              >
                {t(s.labelKey)}
              </button>
            ))}
          </nav>
        </Panel>

        <Panel>
          {section === 'apiKeys' ? (
            <ApiKeysPanel />
          ) : section === 'lookups' ? (
            <LookupSourcesPanel />
          ) : section === 'mailContent' ? (
            <MailContentPanel lockedLocationId={locationBound ? userLocationId : ''} />
          ) : (
          <form onSubmit={onSubmit} className="grid gap-4 sm:grid-cols-2">
            {section === 'general' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">
                    {t('settings.general')}
                  </h2>
                </div>
                <div className="sm:col-span-2">
                  <FieldLabel htmlFor="companyName">Company name</FieldLabel>
                  <TextInput
                    id="companyName"
                    value={form.companyName}
                    onChange={(e) => patchForm({ companyName: e.target.value })}
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="visitorIdPrefix">Visitor ID prefix</FieldLabel>
                  <TextInput
                    id="visitorIdPrefix"
                    value={form.visitorIdPrefix}
                    onChange={(e) => patchForm({ visitorIdPrefix: e.target.value })}
                  />
                </div>
              </>
            ) : null}

            {section === 'branding' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">
                    {t('settings.branding')}
                  </h2>
                </div>
                <div className="sm:col-span-2">
                  <FieldLabel htmlFor="brandLocation">{t('settings.brandLocation')}</FieldLabel>
                  <TextSelect
                    id="brandLocation"
                    value={smtpLocationId}
                    onChange={(e) => setSmtpLocationId(e.target.value)}
                    disabled={loadingSmtp}
                  >
                    {locationBound ? null : <option value="">{t('settings.smtpLocationDefault')}</option>}
                    {locations.map((loc) => (
                      <option key={loc.id} value={loc.id}>
                        {loc.name}
                      </option>
                    ))}
                  </TextSelect>
                  <p className="mt-1 text-xs text-ink-muted">{t('settings.brandLocationHelp')}</p>
                </div>
                <div className="sm:col-span-2">
                  <FieldLabel htmlFor="brandCompanyName">Company name</FieldLabel>
                  <TextInput
                    id="brandCompanyName"
                    value={form.companyName}
                    onChange={(e) => patchForm({ companyName: e.target.value })}
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="themePreset">Theme</FieldLabel>
                  <TextSelect
                    id="themePreset"
                    value={form.themePreset}
                    onChange={(e) => patchForm({ themePreset: e.target.value })}
                  >
                    {THEME_PRESETS.map((theme) => (
                      <option key={theme.id} value={theme.id}>
                        {theme.label}
                      </option>
                    ))}
                  </TextSelect>
                </div>
                <div>
                  <FieldLabel htmlFor="fontPreset">Font</FieldLabel>
                  <TextSelect
                    id="fontPreset"
                    value={form.fontPreset}
                    onChange={(e) => patchForm({ fontPreset: e.target.value })}
                  >
                    {FONT_PRESETS.map((font) => (
                      <option key={font.id} value={font.id} style={{ fontFamily: font.stack }}>
                        {font.label}
                      </option>
                    ))}
                  </TextSelect>
                  <p
                    className="mt-2 rounded-lg border border-border bg-mint/60 px-3 py-2 text-sm text-ink"
                    style={{
                      fontFamily:
                        FONT_PRESETS.find((f) => f.id === form.fontPreset)?.stack ?? FONT_PRESETS[0].stack,
                    }}
                  >
                    The quick brown fox jumps over the lazy dog — 0123456789
                  </p>
                </div>
                <div>
                  <FieldLabel htmlFor="fontSize">Font size</FieldLabel>
                  <TextSelect
                    id="fontSize"
                    value={form.fontSize}
                    onChange={(e) => patchForm({ fontSize: e.target.value })}
                  >
                    {FONT_SIZES.map((size) => (
                      <option key={size.id} value={size.id}>
                        {size.label}
                      </option>
                    ))}
                  </TextSelect>
                  <p className="mt-1 text-xs text-ink-muted">
                    Scales text and spacing across the whole app.
                  </p>
                </div>
              </>
            ) : null}

            {section === 'visitor' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">
                    {t('settings.visitorRules')}
                  </h2>
                </div>
                <div>
                  <FieldLabel htmlFor="maxVisitDurationWarningMinutes">Long-stay warning (minutes)</FieldLabel>
                  <TextInput
                    id="maxVisitDurationWarningMinutes"
                    type="number"
                    value={form.maxVisitDurationWarningMinutes}
                    onChange={(e) => patchForm({ maxVisitDurationWarningMinutes: Number(e.target.value) })}
                  />
                </div>
                <label className="flex min-h-11 items-center gap-2 text-sm sm:col-span-2">
                  <input
                    type="checkbox"
                    checked={form.photoRequired}
                    onChange={(e) => patchForm({ photoRequired: e.target.checked })}
                    className="accent-primary"
                  />
                  Photo required
                </label>
                <label className="flex min-h-11 items-center gap-2 text-sm sm:col-span-2">
                  <input
                    type="checkbox"
                    checked={form.idVerificationRequired}
                    onChange={(e) => patchForm({ idVerificationRequired: e.target.checked })}
                    className="accent-primary"
                  />
                  ID verification required
                </label>
              </>
            ) : null}

            {section === 'pass' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">
                    {t('settings.passSettings')}
                  </h2>
                </div>
                <div className="sm:col-span-2">
                  <FieldLabel htmlFor="passLocation">{t('settings.passLocation')}</FieldLabel>
                  <TextSelect
                    id="passLocation"
                    value={passLocationId}
                    onChange={(e) => setPassLocationId(e.target.value)}
                  >
                    {locationBound ? null : <option value="">{t('settings.smtpLocationDefault')}</option>}
                    {locations.map((loc) => (
                      <option key={loc.id} value={loc.id}>
                        {loc.name}
                      </option>
                    ))}
                  </TextSelect>
                  <p className="mt-1 text-xs text-ink-muted">{t('settings.passLocationHelp')}</p>
                </div>
                <div>
                  <FieldLabel htmlFor="visitorPassValidityHours">Pass validity (hours)</FieldLabel>
                  <TextInput
                    id="visitorPassValidityHours"
                    type="number"
                    value={form.visitorPassValidityHours}
                    onChange={(e) => patchForm({ visitorPassValidityHours: Number(e.target.value) })}
                    disabled={!!passLocationId}
                  />
                </div>
                <div className="sm:col-span-2 border-t border-border/70 pt-4">
                  <h3 className="text-sm font-semibold text-ink">{t('settings.passSeries')}</h3>
                  <p className="mt-1 text-xs text-ink-muted">{t('settings.passSeriesHelp')}</p>
                  {series?.formatExample ? (
                    <p className="mt-1 text-xs text-primary">
                      {t('settings.formatExample')}: {series.formatExample}
                    </p>
                  ) : null}
                </div>
                <div>
                  <FieldLabel htmlFor="passPrefix">{t('settings.prefix')}</FieldLabel>
                  <TextInput
                    id="passPrefix"
                    value={seriesForm.prefix}
                    onChange={(e) => setSeriesForm((f) => ({ ...f, prefix: e.target.value }))}
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="passYear">{t('settings.year')}</FieldLabel>
                  <TextInput
                    id="passYear"
                    value={seriesForm.year}
                    onChange={(e) => setSeriesForm((f) => ({ ...f, year: e.target.value }))}
                    placeholder="e.g. 2026"
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="passStart">{t('settings.startNumber')}</FieldLabel>
                  <TextInput
                    id="passStart"
                    type="number"
                    value={seriesForm.startNumber}
                    onChange={(e) => setSeriesForm((f) => ({ ...f, startNumber: Number(e.target.value) }))}
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="passEnd">{t('settings.endNumber')}</FieldLabel>
                  <TextInput
                    id="passEnd"
                    type="number"
                    value={seriesForm.endNumber}
                    onChange={(e) => setSeriesForm((f) => ({ ...f, endNumber: Number(e.target.value) }))}
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="passCurrent">{t('settings.currentNumber')}</FieldLabel>
                  <TextInput
                    id="passCurrent"
                    type="number"
                    value={seriesForm.currentNumber}
                    onChange={(e) => setSeriesForm((f) => ({ ...f, currentNumber: Number(e.target.value) }))}
                  />
                </div>
                <label className="flex min-h-11 items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={seriesForm.isActive}
                    onChange={(e) => setSeriesForm((f) => ({ ...f, isActive: e.target.checked }))}
                    className="accent-primary"
                  />
                  {t('settings.active')}
                </label>
                <div className="sm:col-span-2">
                  <Button type="button" variant="secondary" loading={passSaving} onClick={() => void saveSeries()}>
                    {t('settings.saveSeries')}
                  </Button>
                </div>

              </>
            ) : null}

            {section === 'email' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">
                    {t('settings.email')}
                  </h2>
                  <p className="mt-1 text-xs text-ink-muted">{t('settings.emailHelp')}</p>
                </div>
                <div className="sm:col-span-2">
                  <FieldLabel htmlFor="smtpLocation">{t('settings.smtpLocation')}</FieldLabel>
                  <TextSelect
                    id="smtpLocation"
                    value={smtpLocationId}
                    onChange={(e) => setSmtpLocationId(e.target.value)}
                  >
                    {locationBound ? null : <option value="">{t('settings.smtpLocationDefault')}</option>}
                    {locations.map((l) => (
                      <option key={l.id} value={l.id}>{l.name}</option>
                    ))}
                  </TextSelect>
                  <p className="mt-1 text-xs text-ink-muted">{t('settings.smtpLocationHelp')}</p>
                </div>
                {loadingSmtp ? (
                  <div className="sm:col-span-2">
                    <Spinner label="Loading location SMTP…" />
                  </div>
                ) : null}
                <label className="flex min-h-11 items-center gap-2 text-sm sm:col-span-2">
                  <input
                    type="checkbox"
                    checked={form.smtpEnabled}
                    onChange={(e) => patchForm({ smtpEnabled: e.target.checked })}
                    className="accent-primary"
                  />
                  {t('settings.smtpEnabled')}
                </label>
                <div className="sm:col-span-2">
                  <FieldLabel htmlFor="smtpHost">{t('settings.smtpHost')}</FieldLabel>
                  <TextInput
                    id="smtpHost"
                    value={form.smtpHost}
                    onChange={(e) => patchForm({ smtpHost: e.target.value })}
                    placeholder="smtp.example.com"
                    autoComplete="off"
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="smtpPort">{t('settings.smtpPort')}</FieldLabel>
                  <TextInput
                    id="smtpPort"
                    type="number"
                    value={form.smtpPort}
                    onChange={(e) => patchForm({ smtpPort: Number(e.target.value) })}
                  />
                </div>
                <label className="flex min-h-11 items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={form.smtpEnableSsl}
                    onChange={(e) => patchForm({ smtpEnableSsl: e.target.checked })}
                    className="accent-primary"
                  />
                  {t('settings.smtpEnableSsl')}
                </label>
                <div>
                  <FieldLabel htmlFor="smtpUsername">{t('settings.smtpUsername')}</FieldLabel>
                  <TextInput
                    id="smtpUsername"
                    value={form.smtpUsername}
                    onChange={(e) => patchForm({ smtpUsername: e.target.value })}
                    autoComplete="off"
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="smtpPassword">{t('settings.smtpPassword')}</FieldLabel>
                  <PasswordInput
                    id="smtpPassword"
                    value={smtpPassword}
                    onChange={(e) => setSmtpPassword(e.target.value)}
                    placeholder={form.smtpPasswordConfigured ? '••••••••' : ''}
                    autoComplete="new-password"
                  />
                  <p className="mt-1 text-xs text-ink-muted">
                    {form.smtpPasswordConfigured
                      ? t('settings.smtpPasswordConfigured')
                      : t('settings.smtpPasswordHelp')}
                  </p>
                </div>
                <div>
                  <FieldLabel htmlFor="smtpFromAddress">{t('settings.smtpFromAddress')}</FieldLabel>
                  <TextInput
                    id="smtpFromAddress"
                    type="email"
                    value={form.smtpFromAddress}
                    onChange={(e) => patchForm({ smtpFromAddress: e.target.value })}
                    placeholder="noreply@example.com"
                  />
                </div>
                <div>
                  <FieldLabel htmlFor="smtpFromName">{t('settings.smtpFromName')}</FieldLabel>
                  <TextInput
                    id="smtpFromName"
                    value={form.smtpFromName}
                    onChange={(e) => patchForm({ smtpFromName: e.target.value })}
                  />
                </div>
                <label className="flex min-h-11 items-center gap-2 text-sm sm:col-span-2">
                  <input
                    type="checkbox"
                    checked={form.smtpIgnoreSslErrors}
                    onChange={(e) => patchForm({ smtpIgnoreSslErrors: e.target.checked })}
                    className="accent-primary"
                  />
                  {t('settings.smtpIgnoreSslErrors')}
                </label>
                <div className="sm:col-span-2 border-t border-border/70 pt-4">
                  <FieldLabel htmlFor="testEmailTo">{t('settings.testEmailTo')}</FieldLabel>
                  <div className="flex flex-col gap-2 sm:flex-row">
                    <TextInput
                      id="testEmailTo"
                      type="email"
                      className="flex-1"
                      value={testEmailTo}
                      onChange={(e) => setTestEmailTo(e.target.value)}
                      placeholder="you@example.com"
                    />
                    <Button
                      type="button"
                      variant="secondary"
                      loading={testingEmail}
                      onClick={() => void sendTestEmail()}
                    >
                      {t('settings.testEmail')}
                    </Button>
                  </div>
                  <div className="mt-3" />
                  <FieldLabel htmlFor="testSendAs">{t('settings.testSendAs')}</FieldLabel>
                  <TextInput
                    id="testSendAs"
                    type="email"
                    value={testSendAs}
                    onChange={(e) => setTestSendAs(e.target.value)}
                    placeholder="employee@example.com"
                  />
                  <p className="mt-1 text-xs text-ink-muted">{t('settings.testSendAsHelp')}</p>
                </div>
              </>
            ) : null}

            {section === 'security' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">
                    {t('settings.security')}
                  </h2>
                </div>
                <div>
                  <FieldLabel htmlFor="sessionTimeoutMinutes">Session timeout (minutes)</FieldLabel>
                  <TextInput
                    id="sessionTimeoutMinutes"
                    type="number"
                    value={form.sessionTimeoutMinutes}
                    onChange={(e) => patchForm({ sessionTimeoutMinutes: Number(e.target.value) })}
                  />
                </div>
              </>
            ) : null}

            <div className="sm:col-span-2">
              <Button type="submit" variant="primary" loading={saving}>
                {saving ? t('common.saving') : t('settings.saveSettings')}
              </Button>
            </div>
          </form>
          )}
        </Panel>

        <Panel className="h-fit text-center">
          <div className="text-sm text-ink-muted">Logo preview</div>
          <BrandLogo
            className="mx-auto mt-3 h-16"
            src={form.logoPath ? assetUrl(form.logoPath) : undefined}
            alt={form.companyName}
          />
          <p className="mt-3 break-all text-xs text-ink-muted">{form.logoPath}</p>
          <input
            ref={fileRef}
            id="logoUpload"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            className="absolute h-px w-px overflow-hidden opacity-0"
            tabIndex={-1}
            onChange={(e) => void onLogoSelected(e.target.files?.[0] ?? null)}
            disabled={uploading}
          />
          <Button
            type="button"
            variant="secondary"
            className="mt-3 w-full"
            loading={uploading}
            onClick={() => fileRef.current?.click()}
          >
            {uploading ? 'Uploading…' : 'Upload new logo'}
          </Button>
          <p className="mt-2 text-xs text-ink-muted">JPEG, PNG or WebP · max 5MB</p>
          <div
            className="mx-auto mt-4 h-10 w-full max-w-[160px] rounded-lg"
            style={{
              background: THEME_PRESETS.find((th) => th.id === form.themePreset)?.swatch
                ?? THEME_PRESETS[0].swatch,
            }}
            aria-hidden
          />
          <p className="mt-2 text-xs text-ink-muted">
            {THEME_PRESETS.find((th) => th.id === form.themePreset)?.label ?? 'Theme'} ·{' '}
            {FONT_PRESETS.find((f) => f.id === form.fontPreset)?.label ?? 'Font'} ·{' '}
            {FONT_SIZES.find((s) => s.id === form.fontSize)?.label ?? 'Medium'}
          </p>
          <button
            type="button"
            className="mt-3 text-xs font-medium text-primary underline-offset-2 hover:underline"
            onClick={() => setSection('branding')}
          >
            Theme & font options
          </button>
        </Panel>
      </div>
    </div>
  )
}
