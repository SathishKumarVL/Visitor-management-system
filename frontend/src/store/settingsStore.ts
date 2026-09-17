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
  visitorIdPrefix: 'TIA',
  visitorPassValidityHours: 12,
  approvalRequired: true,
  walkInApprovalRequired: true,
  photoRequired: false,
  idVerificationRequired: false,
  maxVisitDurationWarningMinutes: 240,
  sessionTimeoutMinutes: 480,
}

interface SettingsState {
  settings: SettingsDto
  logoSrc: string
  loaded: boolean
  load: () => Promise<void>
  update: (next: SettingsDto) => Promise<SettingsDto>
  uploadLogo: (file: File) => Promise<SettingsDto>
}

function applyFromSettings(settings: SettingsDto) {
  applyBranding(settings.themePreset, settings.fontPreset)
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
    }
    applyFromSettings(settings)
    set({
      settings,
      logoSrc: assetUrl(branding.logoPath),
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
  loaded: false,
  load: async () => {
    const token = getStoredToken()
    if (token) {
      try {
        const settings = await settingsApi.get()
        applyFromSettings(settings)
        set({ settings, logoSrc: assetUrl(settings.logoPath), loaded: true })
        return
      } catch {
        await loadBrandingFallback(set, get().settings)
        return
      }
    }

    await loadBrandingFallback(set, get().settings)
  },
  update: async (next) => {
    const settings = await settingsApi.update(next)
    applyFromSettings(settings)
    set({ settings, logoSrc: assetUrl(settings.logoPath) })
    return settings
  },
  uploadLogo: async (file) => {
    const settings = await settingsApi.uploadLogo(file)
    applyFromSettings(settings)
    set({ settings, logoSrc: assetUrl(settings.logoPath) })
    return settings
  },
}))
