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

self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  const data = event.notification.data || {};
  const postId = data.referenceId || data.postId;
  const targetUrl = postId ? `/posts/${postId}` : '/';

  event.waitUntil(
    clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
      for (const client of clientList) {
        const url = new URL(client.url);
        if (url.pathname === targetUrl && 'focus' in client) {
          return client.focus();
        }
      }

      if (clients.openWindow) {
        return clients.openWindow(targetUrl);
      }

      return undefined;
    })
  );
});
