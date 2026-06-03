import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test('global maintenance screen uses remote config message', () {
    final app = File('lib/app.dart').readAsStringSync();
    final providers = File('lib/core/providers.dart').readAsStringSync();
    final remoteConfig =
        File('lib/core/config/remote_config_service.dart').readAsStringSync();
    final screen =
        File('lib/shared/widgets/maintenance_screen.dart').readAsStringSync();

    expect(remoteConfig, contains('RemoteConfigKeys.maintenanceMessage'));
    expect(providers, contains('maintenanceMessageProvider'));
    expect(app, contains('rc.getString(RemoteConfigKeys.maintenanceMessage)'));
    expect(app, contains('message.trim().isEmpty ? null : message.trim()'));
    expect(app, contains('message: maintenanceMessage'));
    expect(screen, contains('final String? message;'));
    expect(screen, contains("message ?? 'Karar birazdan geri dönecek.'"));
  });

  test('force update check is wired before splash navigation', () {
    final splash =
        File('lib/features/home/splash_screen.dart').readAsStringSync();
    final dialog =
        File('lib/core/update/force_update_dialog.dart').readAsStringSync();

    expect(splash, contains('forceUpdateServiceProvider'));
    expect(splash, contains('checkForUpdate()'));
    expect(splash, contains('ForceUpdateDialog.showIfNeeded'));
    expect(dialog, contains('barrierDismissible: false'));
    expect(dialog, contains('canPop: false'));
  });
}
