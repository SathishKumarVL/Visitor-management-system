import { notificationsApi } from './api'

/** 'needs-install': iPhone / iPad only allow push from the app added to the Home Screen. */
export type PushState = 'unsupported' | 'needs-install' | 'denied' | 'off' | 'on'

const SW_URL = '/sw.js'

export function pushSupported(): boolean {
  return (
    typeof window !== 'undefined' &&
    window.isSecureContext &&
    'serviceWorker' in navigator &&
    'PushManager' in window &&
    'Notification' in window
  )
}

function isIosBrowserTab(): boolean {
  const ios =
    /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1)
  const standalone =
    window.matchMedia('(display-mode: standalone)').matches ||
    (navigator as Navigator & { standalone?: boolean }).standalone === true
  return ios && !standalone
}

function keyBytes(base64Url: string): Uint8Array<ArrayBuffer> {
  const base64 = (base64Url + '='.repeat((4 - (base64Url.length % 4)) % 4)).replace(/-/g, '+').replace(/_/g, '/')
  const raw = atob(base64)
  const bytes = new Uint8Array(new ArrayBuffer(raw.length))
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i)
  return bytes
}

function sameKey(current: ArrayBuffer | null | undefined, publicKey: string): boolean {
  if (!current) return false
  const a = new Uint8Array(current)
  const b = keyBytes(publicKey)
  return a.length === b.length && a.every((v, i) => v === b[i])
}

/** Registered on every load so Chrome / Edge offer "Install app" for the PWA. */
export function registerServiceWorker(): void {
  if (typeof window === 'undefined' || !window.isSecureContext || !('serviceWorker' in navigator)) return
  window.addEventListener('load', () => {
    navigator.serviceWorker.register(SW_URL, { scope: '/' }).catch(() => undefined)
  })
}

async function currentSubscription(): Promise<PushSubscription | null> {
  const registration = await navigator.serviceWorker.getRegistration('/')
  return (await registration?.pushManager.getSubscription()) ?? null
}

async function sendToServer(subscription: PushSubscription) {
  const json = subscription.toJSON()
  await notificationsApi.subscribe({
    endpoint: subscription.endpoint,
    p256dh: json.keys?.p256dh ?? '',
    auth: json.keys?.auth ?? '',
  })
}

export async function getPushState(): Promise<PushState> {
  if (!pushSupported()) return isIosBrowserTab() ? 'needs-install' : 'unsupported'
  if (Notification.permission === 'denied') return 'denied'
  if (Notification.permission !== 'granted') return 'off'
  return (await currentSubscription()) ? 'on' : 'off'
}

/** Must run from a click: browsers only show the permission prompt for a user gesture. */
export async function enablePush(): Promise<void> {
  const permission = await Notification.requestPermission()
  if (permission !== 'granted') {
    throw new Error('Notifications were not allowed. Allow them for this site in the browser settings.')
  }
  const registration =
    (await navigator.serviceWorker.getRegistration('/')) ?? (await navigator.serviceWorker.register(SW_URL, { scope: '/' }))
  await navigator.serviceWorker.ready
  const { publicKey } = await notificationsApi.pushStatus()

  let subscription = await registration.pushManager.getSubscription()
  // A subscription made for another server key cannot receive this server's pushes.
  if (subscription && !sameKey(subscription.options.applicationServerKey, publicKey)) {
    await subscription.unsubscribe()
    subscription = null
  }
  subscription ??= await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: keyBytes(publicKey),
  })
  await sendToServer(subscription)
}

export async function disablePush(): Promise<void> {
  const subscription = await currentSubscription()
  if (!subscription) return
  await notificationsApi.unsubscribe(subscription.endpoint).catch(() => undefined)
  await subscription.unsubscribe()
}

/** After sign-in: a browser that already has notifications on delivers them to whoever is signed in now. */
export async function syncPushSubscription(): Promise<void> {
  if (!pushSupported() || Notification.permission !== 'granted') return
  const subscription = await currentSubscription()
  if (!subscription) return
  const { publicKey } = await notificationsApi.pushStatus()
  if (!sameKey(subscription.options.applicationServerKey, publicKey)) {
    await subscription.unsubscribe()
    return
  }
  await sendToServer(subscription)
}

/** On sign-out: stop delivering to this browser until someone signs in again. */
export async function detachPushSubscription(): Promise<void> {
  if (!pushSupported()) return
  const subscription = await currentSubscription()
  if (subscription) await notificationsApi.unsubscribe(subscription.endpoint).catch(() => undefined)
}
