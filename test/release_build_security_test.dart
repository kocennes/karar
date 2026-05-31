import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  group('Release build security gate', () {
    test('build-release.ps1 uses Flutter obfuscation flags', () {
      final script = File('scripts/build-release.ps1').readAsStringSync();

      expect(
        script,
        contains('--obfuscate'),
        reason:
            'Release build must pass --obfuscate to Flutter. '
            'Remove this flag only if you have an explicit security review approval.',
      );
      expect(
        script,
        contains('--split-debug-info'),
        reason:
            'Release build must split debug info via --split-debug-info. '
            'This keeps symbols off the App Store binary.',
      );
    });

    test('Android release build type has minification enabled', () {
      final gradle =
          File('android/app/build.gradle.kts').readAsStringSync();

      expect(
        gradle,
        contains('isMinifyEnabled = true'),
        reason: 'Android release buildType must enable R8/ProGuard minification.',
      );
      expect(
        gradle,
        contains('isShrinkResources = true'),
        reason: 'Android release buildType must shrink unused resources.',
      );
    });

    test('ProGuard rules file is present', () {
      expect(
        File('android/app/proguard-rules.pro').existsSync(),
        isTrue,
        reason: 'proguard-rules.pro must exist alongside build.gradle.kts.',
      );
    });
  });
}
