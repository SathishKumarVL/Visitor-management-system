import { useEffect, useRef, useState, type FormEvent } from 'react'
import { apiErrorMessage } from '../lib/api'
import type { SettingsDto } from '../types/api'
import { Alert, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, TextSelect } from '../components/ui/Field'
import { useSettingsStore } from '../store/settingsStore'
import { BrandLogo } from '../components/BrandLogo'
import { cn } from '../lib/utils'
import { FONT_PRESETS, THEME_PRESETS, applyBranding } from '../lib/branding'

const SECTIONS = [
  { id: 'general', label: 'General' },
  { id: 'branding', label: 'Branding' },
  { id: 'visitor', label: 'Visitor Rules' },
  { id: 'pass', label: 'Pass Settings' },
  { id: 'security', label: 'Security' },
] as const

export function SettingsPage() {
  const { settings, load, update, uploadLogo, loaded } = useSettingsStore()
  const [form, setForm] = useState<SettingsDto>(settings)
  const [saving, setSaving] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [section, setSection] = useState<(typeof SECTIONS)[number]['id']>('general')
  const fileRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    setForm(settings)
  }, [settings])

  function patchForm(partial: Partial<SettingsDto>) {
    const next = { ...form, ...partial }
    setForm(next)
    if (partial.themePreset !== undefined || partial.fontPreset !== undefined) {
      applyBranding(next.themePreset, next.fontPreset)
    }
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError(null)
    setMessage(null)
    try {
      await update({
        ...form,
        approvalRequired: false,
        walkInApprovalRequired: false,
      })
      setMessage('Settings saved.')
    } catch (err) {
      setError(apiErrorMessage(err, 'Unable to save settings.'))
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
      const next = await uploadLogo(file)
      setForm(next)
      setMessage('Logo updated.')
    } catch (err) {
      setError(apiErrorMessage(err, 'Unable to upload logo.'))
    } finally {
      setUploading(false)
      if (fileRef.current) fileRef.current.value = ''
    }
  }

  if (!loaded) return <Spinner />

  return (
    <div>
      <PageHeader eyebrow="Administration" title="Settings" subtitle="Company branding and visitor policy" />
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
            {SECTIONS.map((s) => (
              <button
                key={s.id}
                type="button"
                onClick={() => setSection(s.id)}
                className={cn(
                  'min-h-11 rounded-xl px-3 text-left text-sm font-medium transition',
                  section === s.id ? 'bg-brand-gradient text-white shadow-md' : 'text-ink-muted hover:bg-mint',
                )}
              >
                {s.label}
              </button>
            ))}
          </nav>
        </Panel>

        <Panel>
          <form onSubmit={onSubmit} className="grid gap-4 sm:grid-cols-2">
            {section === 'general' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">General</h2>
                </div>
                <div>
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
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Branding</h2>
                </div>

                <div className="sm:col-span-2 space-y-2">
                  <FieldLabel htmlFor="logoUpload">Company logo</FieldLabel>
                  <p className="text-xs text-ink-muted">
                    Upload a JPEG, PNG, or WebP image (max 5MB). Shown on login and in the app header.
                  </p>
                  <div className="flex flex-wrap items-center gap-3">
                    <input
                      ref={fileRef}
                      id="logoUpload"
                      type="file"
                      accept="image/jpeg,image/png,image/webp"
                      className="block w-full max-w-md text-sm text-ink-muted file:mr-3 file:rounded-lg file:border-0 file:bg-mint file:px-3 file:py-2 file:text-sm file:font-medium file:text-ink"
                      onChange={(e) => void onLogoSelected(e.target.files?.[0] ?? null)}
                      disabled={uploading}
                    />
                    {uploading ? <span className="text-sm text-ink-muted">Uploading…</span> : null}
                  </div>
                </div>

                <div className="sm:col-span-2">
                  <FieldLabel>Theme</FieldLabel>
                  <p className="mb-2 text-xs text-ink-muted">Pick a color theme for the application.</p>
                  <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
                    {THEME_PRESETS.map((theme) => {
                      const selected = form.themePreset === theme.id
                      return (
                        <button
                          key={theme.id}
                          type="button"
                          onClick={() => patchForm({ themePreset: theme.id })}
                          className={cn(
                            'rounded-xl border p-3 text-left transition',
                            selected
                              ? 'border-primary ring-2 ring-primary/30'
                              : 'border-border hover:border-primary/40',
                          )}
                        >
                          <div
                            className="mb-2 h-8 w-full rounded-lg"
                            style={{ background: theme.swatch }}
                            aria-hidden
                          />
                          <div className="text-sm font-medium text-ink">{theme.label}</div>
                          <div className="text-xs text-ink-muted">{theme.description}</div>
                        </button>
                      )
                    })}
                  </div>
                </div>

                <div className="sm:col-span-2">
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
              </>
            ) : null}

            {section === 'visitor' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Visitor Rules</h2>
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
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Pass Settings</h2>
                </div>
                <div>
                  <FieldLabel htmlFor="visitorPassValidityHours">Pass validity (hours)</FieldLabel>
                  <TextInput
                    id="visitorPassValidityHours"
                    type="number"
                    value={form.visitorPassValidityHours}
                    onChange={(e) => patchForm({ visitorPassValidityHours: Number(e.target.value) })}
                  />
                </div>
              </>
            ) : null}

            {section === 'security' ? (
              <>
                <div className="sm:col-span-2">
                  <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Security</h2>
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
                {saving ? 'Saving…' : 'Save settings'}
              </Button>
            </div>
          </form>
        </Panel>

        <Panel className="h-fit text-center">
          <div className="text-sm text-ink-muted">Logo preview</div>
          <BrandLogo className="mx-auto mt-3 h-16" />
          <p className="mt-3 break-all text-xs text-ink-muted">{form.logoPath}</p>
          <div
            className="mx-auto mt-4 h-10 w-full max-w-[160px] rounded-lg"
            style={{
              background: THEME_PRESETS.find((t) => t.id === form.themePreset)?.swatch
                ?? THEME_PRESETS[0].swatch,
            }}
            aria-hidden
          />
          <p className="mt-2 text-xs text-ink-muted">
            {THEME_PRESETS.find((t) => t.id === form.themePreset)?.label ?? 'Theme'} ·{' '}
            {FONT_PRESETS.find((f) => f.id === form.fontPreset)?.label ?? 'Font'}
          </p>
        </Panel>
      </div>
    </div>
  )
}
