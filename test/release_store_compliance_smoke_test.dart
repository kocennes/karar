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
      expect(postDetail, contains('ContentUnavailableView'));
      expect(postDetail, contains('under_review'));
      expect(postDetail, contains('auto_hidden'));
      expect(postDetail, contains('_confirmBlock'));
      expect(authService, contains('/blocked'));
      expect(settings, contains('/settings/blocked-users'));
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
