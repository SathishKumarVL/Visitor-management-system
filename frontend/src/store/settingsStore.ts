import { create } from 'zustand'
import { getStoredToken, settingsApi } from '../lib/api'
import type { SettingsDto } from '../types/api'
import { assetUrl } from '../lib/utils'
import { applyBranding } from '../lib/branding'

const defaults: SettingsDto = {
  companyName: 'TIAANO',
  logoPath: '/branding/tiaano-logo.png',
  themePreset: 'tiaano',
  fontPreset: 'inter',
  fontSize: 'medium',
  visitorIdPrefix: 'TIA',
  visitorPassValidityHours: 12,
  approvalRequired: true,
  walkInApprovalRequired: true,
  photoRequired: false,
  idVerificationRequired: false,
  maxVisitDurationWarningMinutes: 240,
  sessionTimeoutMinutes: 480,
  smtpEnabled: true,
  smtpHost: '',
  smtpPort: 587,
  smtpEnableSsl: true,
  smtpUsername: '',
  smtpFromAddress: '',
  smtpFromName: 'Visitor Management',
  smtpIgnoreSslErrors: false,
  smtpPassword: null,
  smtpPasswordConfigured: false,
}

interface SettingsState {
  /** Tenant-level settings (what the Settings page edits). */
  settings: SettingsDto
  /** Displayed branding — follows the signed-in user's location when it has overrides. */
  logoSrc: string
  companyName: string
  loaded: boolean
  load: () => Promise<void>
  refreshDisplayBranding: () => Promise<void>
  update: (next: SettingsDto, locationId?: string) => Promise<SettingsDto>
  uploadLogo: (file: File, locationId?: string) => Promise<SettingsDto>
}

function applyFromSettings(settings: SettingsDto) {
  applyBranding(settings.themePreset, settings.fontPreset, settings.fontSize)
}

function displayFrom(b: { companyName: string; logoPath: string }) {
  return { companyName: b.companyName, logoSrc: assetUrl(b.logoPath) }
}

async function loadBrandingFallback(
  set: (partial: Partial<SettingsState>) => void,
  current: SettingsDto,
) {
  try {
    const branding = await settingsApi.getBranding()
    const settings: SettingsDto = {
      ...current,
      companyName: branding.companyName,
      logoPath: branding.logoPath,
      themePreset: branding.themePreset || current.themePreset,
      fontPreset: branding.fontPreset || current.fontPreset,
      fontSize: branding.fontSize || current.fontSize,
    }
    applyFromSettings(settings)
    set({
      settings,
      ...displayFrom(branding),
      loaded: true,
    })
  } catch {
    applyFromSettings(current)
    set({ loaded: true })
  }
}

export const useSettingsStore = create<SettingsState>((set, get) => ({
  settings: defaults,
  logoSrc: assetUrl(defaults.logoPath),
  companyName: defaults.companyName,
  loaded: false,
  load: async () => {
    const token = getStoredToken()
    if (token) {
      try {
        const settings = await settingsApi.get()
        applyFromSettings(settings)
        set({ settings, ...displayFrom(settings), loaded: true })
        await get().refreshDisplayBranding()
        return
      } catch {
        await loadBrandingFallback(set, get().settings)
        return
      }
    }

    await loadBrandingFallback(set, get().settings)
  },
  refreshDisplayBranding: async () => {
    if (!getStoredToken()) return
    try {
      const branding = await settingsApi.getCurrentBranding()
      applyBranding(branding.themePreset, branding.fontPreset, branding.fontSize)
      set(displayFrom(branding))
    } catch {
      // Keep tenant branding already shown.
    }
  },
  update: async (next, locationId) => {
    const settings = await settingsApi.update(next, locationId)
    if (!locationId) set({ settings })
    await get().refreshDisplayBranding()
    return settings
  },
  uploadLogo: async (file, locationId) => {
    const settings = await settingsApi.uploadLogo(file, locationId)
    if (!locationId) set({ settings })
    await get().refreshDisplayBranding()
    return settings
  },
}))
