import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  group('PrivacySummaryScreen contract', () {
    test('screen contains all three required sections', () {
      final screen = _read('lib/features/legal/privacy_summary_screen.dart');

      expect(screen, contains('Ne topluyoruz?'));
      expect(screen, contains('Neden topluyoruz?'));
      expect(screen, contains('Ne kadar süre saklıyoruz?'));
    });

    test('screen content aligns with docs/privacy.md data points', () {
      final screen = _read('lib/features/legal/privacy_summary_screen.dart');

      // Veri tipleri
      expect(screen, contains('Cihaz'));
      expect(screen, contains('IP'));
      expect(screen, contains('FCM'));

      // Saklama süreleri
      expect(screen, contains('90 gün'));
      expect(screen, contains('30 gün'));
    });

    test('route /legal/privacy-summary is registered in router', () {
      final router = _read('lib/core/router/app_router.dart');

      expect(router, contains('/legal/privacy-summary'));
      expect(router, contains('PrivacySummaryScreen'));
    });

    test('settings screen has privacy summary tile linking to route', () {
      final settings = _read('lib/features/settings/settings_screen.dart');

      expect(settings, contains('Gizlilik Özeti'));
      expect(settings, contains('/legal/privacy-summary'));
    });

    test('screen uses theme colors not hardcoded colors', () {
      final screen = _read('lib/features/legal/privacy_summary_screen.dart');

      expect(screen, isNot(contains('Color(0x')));
      expect(screen, isNot(contains('Colors.red')));
      expect(screen, isNot(contains('Colors.blue')));
    });

    test('screen text wraps correctly (no fixed width constraints)', () {
      final screen = _read('lib/features/legal/privacy_summary_screen.dart');

      // Expanded veya Flexible kullanılıyor, fixed width değil
      expect(screen, contains('Expanded'));
    });

    test('KVKK contact address is present', () {
      final screen = _read('lib/features/legal/privacy_summary_screen.dart');

      expect(screen, contains('kvkk@karar.app'));
    });
  });
}

String _read(String path) => File(path).readAsStringSync();
