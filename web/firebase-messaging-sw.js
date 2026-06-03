importScripts('https://www.gstatic.com/firebasejs/10.12.5/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/10.12.5/firebase-messaging-compat.js');

try {
  importScripts('/firebase-config.js');
} catch (_) {
  // Local/dev builds can run without Firebase Web config.
}

const firebaseConfig = self.firebaseConfig || {
  apiKey: '',
  authDomain: '',
  projectId: '',
  storageBucket: '',
  messagingSenderId: '',
  appId: '',
};

if (firebaseConfig.apiKey) {
  firebase.initializeApp(firebaseConfig);

  const messaging = firebase.messaging();

  messaging.onBackgroundMessage((payload) => {
    const notification = payload.notification || {};
    const data = payload.data || {};

    self.registration.showNotification(notification.title || 'Karar', {
      body: notification.body || '',
      icon: '/icons/Icon-192.png',
      badge: '/icons/Icon-192.png',
      data,
    });
  });
}

function targetUrlForNotification(data) {
  const deepLink = data.deepLink || data.deeplink;
  if (deepLink && deepLink.startsWith('/')) {
    return deepLink;
  }

  const postId = data.postId || data.referenceId;
  if (postId) {
    const suffix = data.commentId
      ? `?commentId=${encodeURIComponent(data.commentId)}&source=notification`
      : '?source=notification';
    return `/posts/${encodeURIComponent(postId)}${suffix}`;
  }

  return '/notifications';
}

self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  const data = event.notification.data || {};
  const targetUrl = targetUrlForNotification(data);

  event.waitUntil(
    clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
      const target = new URL(targetUrl, self.location.origin);

      for (const client of clientList) {
        const url = new URL(client.url);
        if (url.origin === target.origin && 'focus' in client) {
          if (url.pathname === target.pathname && url.search === target.search) {
            return client.focus();
          }

          if ('navigate' in client) {
            return client.navigate(target.href).then((navigatedClient) => {
              if (navigatedClient && 'focus' in navigatedClient) {
                return navigatedClient.focus();
              }
              return client.focus();
            });
          }
        }
      }

      if (clients.openWindow) {
        return clients.openWindow(target.href);
      }

      return undefined;
    })
  );
});
