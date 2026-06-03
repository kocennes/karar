import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  group('web push production contract', () {
    test('requires a VAPID key for secure web builds', () {
      final appConfig =
          File('lib/core/config/app_config.dart').readAsStringSync();

      expect(
          appConfig, contains("String.fromEnvironment(\n    'WEB_VAPID_KEY'"));
      expect(appConfig, contains('kIsWeb && requireSecureApiTransport'));
      expect(appConfig, contains('webVapidKey.isEmpty'));
      expect(appConfig, contains('Production WEB_VAPID_KEY must be set'));
    });

    test('uses VAPID when registering Firebase web tokens', () {
      final service = File(
        'lib/core/notifications/notification_service.dart',
      ).readAsStringSync();

      expect(service, contains('FirebaseMessaging.instance.getToken'));
      expect(service,
          contains('vapidKey: kIsWeb && AppConfig.webVapidKey.isNotEmpty'));
      expect(service, contains('deviceService.registerFcmToken(token)'));
      expect(service, contains('deviceService.deleteFcmToken()'));
      expect(service, contains('FirebaseMessaging.instance.deleteToken()'));
      expect(service, contains('if (kIsWeb) return;'));
      expect(service, contains('canOpenPlatformNotificationSettings'));
      expect(service, contains('deniedPermissionHelpText'));
      expect(service, contains('Tarayici site ayarlarindan'));
    });

    test('logout deletes the current web push token centrally', () {
      final authService =
          File('lib/core/auth/auth_service.dart').readAsStringSync();
      final appServices = File('lib/core/app_services.dart').readAsStringSync();

      expect(authService, contains('Future<void> Function()? _onBeforeLogout'));
      expect(authService, contains('setOnBeforeLogout'));
      expect(authService, contains('await _onBeforeLogout?.call();'));
      expect(
          appServices,
          contains(
              'authService.setOnBeforeLogout(notificationService.deleteCurrentToken)'));
    });

    test('registers web tokens with a web platform marker', () {
      final deviceService = File(
        'lib/core/auth/device_service.dart',
      ).readAsStringSync();

      expect(deviceService, contains("platform': _platform()"));
      expect(deviceService, contains('if (kIsWeb)'));
      expect(deviceService, contains("return 'web';"));
      expect(deviceService, contains("ApiEndpoints.fcmToken"));
    });

    test('service worker opens notification deep links', () {
      final worker = File('web/firebase-messaging-sw.js').readAsStringSync();

      expect(worker, contains("importScripts('/firebase-config.js')"));
      expect(worker, contains('messaging.onBackgroundMessage'));
      expect(worker, contains("data.deepLink || data.deeplink"));
      expect(worker, contains("return '/notifications';"));
      expect(worker, contains("client.navigate(target.href)"));
      expect(worker, contains("clients.openWindow(target.href)"));
      expect(worker, contains("source=notification"));
    });
  });
}
