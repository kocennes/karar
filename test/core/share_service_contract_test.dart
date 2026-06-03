import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test('ShareService uses share result and clipboard fallback contract', () {
    final service =
        File('lib/core/utils/share_service.dart').readAsStringSync();

    expect(service, contains('final ShareResult result'));
    expect(service, contains('result = await Share.share'));
    expect(service, contains('try {'));
    expect(service, contains('catch (_)'));
    expect(service, contains('ShareResultStatus.success'));
    expect(service, contains('ShareResultStatus.unavailable'));
    expect(service, contains('Clipboard.setData'));
    expect(service, contains('logPostShared'));
  });

  test('SharePickerSheet falls back to clipboard when web share is unavailable',
      () {
    final sheet = File('lib/features/post_detail/share_picker_sheet.dart')
        .readAsStringSync();

    expect(sheet, contains('ShareResultStatus.unavailable'));
    expect(sheet, contains('_copyShareLinkFallback'));
    expect(sheet, contains('Clipboard.setData'));
    expect(sheet, contains('_showClipboardFallback'));
  });
}
