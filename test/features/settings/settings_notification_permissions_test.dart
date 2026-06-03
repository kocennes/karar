import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test('notification sound action opens platform notification settings', () {
    final settings =
        File('lib/features/settings/settings_screen.dart').readAsStringSync();

    expect(settings, contains("title: 'Bildirim sesi'"));
    expect(settings, contains('notificationServiceProvider'));
    expect(settings, contains('openSettings()'));
    expect(settings, contains('Ses ve kanal ayarını cihazdan yönet'));
  });
  test('push toggle updates preference and platform token state', () {
    final settings =
        File('lib/features/settings/settings_screen.dart').readAsStringSync();

    expect(settings, contains('_setPushEnabled'));
    expect(settings, contains('copyWith(pushEnabled: enabled)'));
    expect(settings, contains('deleteCurrentToken()'));
    expect(settings, contains('maybeRequestPermission(force: true)'));
  });

  test('denied web push state shows browser settings guidance', () {
    final settings =
        File('lib/features/settings/settings_screen.dart').readAsStringSync();

    expect(settings, contains('deniedPermissionHelpText'));
    expect(settings, contains('canOpenPlatformNotificationSettings'));
    expect(settings, contains('notifications.openSettings()'));
  });
}
