const CACHE_NAME = 'unity-webgl-cache-v1';
const FILES_TO_CACHE = [
  '/Build/Web.loader.js',
  '/Build/Web.framework.js',
  '/Build/Web.data',
  '/Build/Web.wasm',
  '/TemplateData/style.css'
];

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => cache.addAll(FILES_TO_CACHE))
  );
  self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys =>
      Promise.all(
        keys.filter(key => key !== CACHE_NAME)
            .map(key => caches.delete(key))
      )
    )
  );
  self.clients.claim();
});

self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET') return;
  if (
    event.request.url.includes('/Build/') ||
    event.request.url.includes('/TemplateData/')
  ) {
    event.respondWith(
      caches.open(CACHE_NAME).then(cache =>
        cache.match(event.request).then(response =>
          response ||
          fetch(event.request).then(networkResponse => {
            if (networkResponse.ok) {
              cache.put(event.request, networkResponse.clone());
            }
            return networkResponse;
          })
        )
      )
    );
  }
});
