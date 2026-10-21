// Push notifications only: no caching, so a new deployment is never served stale.
self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))

self.addEventListener('push', (event) => {
  let data = {}
  try {
    data = event.data ? event.data.json() : {}
  } catch {
    data = { body: event.data ? event.data.text() : '' }
  }
  const title = data.title || 'Tiaano VMS'
  event.waitUntil(
    self.registration.showNotification(title, {
      body: data.body || '',
      icon: '/branding/tiaano-icon-192.png',
      badge: '/branding/tiaano-badge-96.png',
      tag: data.tag || undefined,
      data: { url: data.url || '/' },
    }),
  )
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const target = new URL(event.notification.data?.url || '/', self.location.origin)
  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
      const open = windows.find((w) => new URL(w.url).origin === self.location.origin)
      if (open) {
        await open.focus()
        // The app routes in place (see usePushNavigation) instead of reloading.
        open.postMessage({ type: 'open-url', url: target.pathname + target.search })
        return undefined
      }
      return self.clients.openWindow(target.href)
    })(),
  )
})
