import 'package:app_settings/app_settings.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../analytics/analytics_service.dart';
import '../auth/device_service.dart';
import '../config/app_config.dart';

class NotificationService {
  NotificationService({required this.deviceService, required this.analytics});

  final DeviceService deviceService;
  final AnalyticsService analytics;

  void Function(RemoteMessage)? onForegroundMessage;

  static const _kInteractionsKey = 'notif_interactions';
  static const _kDecidedKey = 'notif_permission_decided';
  static const _kSoftPromptDismissedKey = 'notif_soft_prompt_dismissed_at';
  static const _kSoftPromptCooldownDays = 7;
  static const _kThreshold = 3;

  Future<void> init() async {
    final fcm = FirebaseMessaging.instance;

    // If permission was already granted in a previous session, register token now.
    final settings = await fcm.getNotificationSettings();
    if (settings.authorizationStatus == AuthorizationStatus.authorized ||
        settings.authorizationStatus == AuthorizationStatus.provisional) {
      final token = await fcm.getToken(
        vapidKey: kIsWeb && AppConfig.webVapidKey.isNotEmpty
            ? AppConfig.webVapidKey
            : null,
      );
      if (token != null) await _registerToken(token);
      _markDecided();
    }

    // Always listen for token refresh so we re-register if token rotates.
    fcm.onTokenRefresh.listen(_registerToken);

    // Show notification banner while app is in foreground on iOS.
    if (!kIsWeb) {
      await fcm.setForegroundNotificationPresentationOptions(
        alert: true,
        badge: true,
        sound: true,
      );
    }

    FirebaseMessaging.onMessage.listen((RemoteMessage message) {
      if (kDebugMode) {
        debugPrint('Foreground push: ${message.notification?.title}');
      }
      onForegroundMessage?.call(message);
    });
  }

  Future<bool> isPermissionDecided() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getBool(_kDecidedKey) == true;
  }

  Future<bool> isSoftPromptOnCooldown() async {
    final prefs = await SharedPreferences.getInstance();
    final ms = prefs.getInt(_kSoftPromptDismissedKey);
    if (ms == null) return false;
    final dismissed = DateTime.fromMillisecondsSinceEpoch(ms);
    return DateTime.now().difference(dismissed).inDays < _kSoftPromptCooldownDays;
  }

  Future<void> recordSoftPromptDismissed() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setInt(
        _kSoftPromptDismissedKey, DateTime.now().millisecondsSinceEpoch);
  }

  Future<void> markPermissionDecided() async => _markDecided();

  Future<bool> isDenied() async {
    final settings = await FirebaseMessaging.instance.getNotificationSettings();
    return settings.authorizationStatus == AuthorizationStatus.denied;
  }

  bool get canOpenPlatformNotificationSettings => !kIsWeb;

  String get deniedPermissionHelpText => kIsWeb
      ? 'Tarayici site ayarlarindan bildirim iznini ac. In-app bildirim merkezi calismaya devam eder.'
      : 'Bildirimler kapali. Istersen cihaz ayarlarindan tekrar acabilirsin.';

  Future<void> openSettings() async {
    if (kIsWeb) return;
    await AppSettings.openAppSettings(type: AppSettingsType.notification);
  }

  Future<void> deleteCurrentToken() async {
    try {
      await deviceService.deleteFcmToken();
      await FirebaseMessaging.instance.deleteToken();
    } catch (_) {}
  }

  // Call this after a meaningful user interaction (vote, post creation).
  // Increments the interaction counter; requests permission once threshold is hit.
  // Set force=true to ignore threshold (e.g. AHA moment).
  Future<void> maybeRequestPermission({bool force = false}) async {
    final prefs = await SharedPreferences.getInstance();
    if (prefs.getBool(_kDecidedKey) == true) return;

    final count = (prefs.getInt(_kInteractionsKey) ?? 0) + 1;
    await prefs.setInt(_kInteractionsKey, count);

    if (!force && count < _kThreshold) return;

    final settings = await FirebaseMessaging.instance.requestPermission(
      alert: true,
      badge: true,
      sound: true,
    );
    _markDecided();

    final granted =
        settings.authorizationStatus == AuthorizationStatus.authorized ||
            settings.authorizationStatus == AuthorizationStatus.provisional;

    if (granted) {
      analytics.logNotificationPermissionGranted(
          source: force ? 'aha_moment' : 'threshold');
      final token = await FirebaseMessaging.instance.getToken(
        vapidKey: kIsWeb && AppConfig.webVapidKey.isNotEmpty
            ? AppConfig.webVapidKey
            : null,
      );
      if (token != null) await _registerToken(token);
    } else {
      analytics.logNotificationPermissionDenied(
          source: force ? 'aha_moment' : 'threshold');
    }
  }

  Future<void> _markDecided() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_kDecidedKey, true);
  }

  Future<void> _registerToken(String token) async {
    try {
      await deviceService.registerFcmToken(token);
    } catch (_) {}
  }
}
