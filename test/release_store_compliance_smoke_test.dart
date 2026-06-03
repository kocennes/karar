import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  group('UGC store compliance smoke gate', () {
    test('registration requires terms and community guideline acceptance', () {
      final screen = _read('lib/features/auth/register_screen.dart');
      final authService = _read('lib/core/auth/auth_service.dart');

      expect(screen, contains('CheckboxListTile'));
      expect(screen, contains('/legal/terms'));
      expect(screen, contains('/legal/community'));
      expect(authService, contains('acceptedTerms'));
      expect(authService, contains('acceptedCommunityGuidelines'));
    });

    test('post creation requires policy acceptance before publish', () {
      final screen = _read('lib/features/create_post/create_post_screen.dart');
      final provider =
          _read('lib/features/create_post/create_post_provider.dart');
      final repository = _read('lib/features/feed/post_repository.dart');

      expect(screen, contains('CheckboxListTile'));
      expect(screen, contains('/legal/terms'));
      expect(screen, contains('/legal/community'));
      expect(provider, contains('acceptedTerms'));
      expect(provider, contains('acceptedCommunityGuidelines'));
      expect(repository, contains("'acceptedTerms'"));
      expect(repository, contains("'acceptedCommunityGuidelines'"));
    });

    test('reporting, blocking and removed deep link flows are present', () {
      final report = _read('lib/features/report/report_bottom_sheet.dart');
      final postDetail =
          _read('lib/features/post_detail/post_detail_screen.dart');
      final authService = _read('lib/core/auth/auth_service.dart');
      final settings = _read('lib/features/settings/settings_screen.dart');

      expect(report, contains('widget.repository.report'));
      expect(report, contains('/legal/community'));
      expect(report, contains("'personal_info'"));
      expect(report, contains("'misinformation'"));
      expect(report, contains("'illegal'"));
      expect(report, isNot(contains("'doxxing'")));
      expect(report, isNot(contains("'fake_story'")));
      expect(report, isNot(contains("'illegal_content'")));
      expect(postDetail, contains('ContentUnavailableView'));
      expect(postDetail, contains('under_review'));
      expect(postDetail, contains('auto_hidden'));
      expect(postDetail, contains('_confirmBlock'));
      expect(authService, contains('/blocked'));
      expect(settings, contains('/settings/blocked-users'));
    });

    test('account deletion flow satisfies App Store 5.1.1 and KVKK', () {
      final settings = _read('lib/features/settings/settings_screen.dart');
      final authService = _read('lib/core/auth/auth_service.dart');

      expect(settings, contains('deleteAccount'),
          reason: 'Settings screen must call deleteAccount');
      expect(settings, contains('/auth/login'),
          reason: 'After deletion, user must be redirected to login');
      expect(settings, contains('anonim'),
          reason: 'Dialog must explain content anonymisation (KVKK)');
      expect(settings, contains('30 gün'),
          reason: 'Dialog must mention 30-day grace period');
      expect(settings, contains('Oturumun'),
          reason: 'Dialog must state the session will be closed');
      expect(authService, contains('deleteJson'),
          reason: 'AuthService must call DELETE /api/v1/users/me via deleteJson');
      expect(authService, contains('clearAuthTokens'),
          reason: 'AuthService must clear tokens after deletion');
    });

    test('demo account and smoke release command are documented', () {
      final releaseDoc = _read('docs/app-store-release.md');
      final script = _read('scripts/run-store-compliance-smoke.ps1');

      expect(releaseDoc, contains('reviewer@karar.app'));
      expect(releaseDoc, contains('scripts/run-store-compliance-smoke.ps1'));
      expect(script, contains('store-compliance-smoke'));
    });
  });
}

String _read(String path) => File(path).readAsStringSync();
