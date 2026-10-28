import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/theme/app_theme.dart';
import 'package:karar/core/theme/app_typography.dart';
import 'package:karar/shared/widgets/offline_view.dart';

/// The web app must reach its first frame without any external host other
/// than the API: fonts come from the bundle and no third-party SDK can block
/// `runApp` indefinitely (tracker blockers, filtered or very slow networks).
void main() {
  test('text renders from bundled fonts, never fetched at runtime', () {
    final pubspec = File('pubspec.yaml').readAsStringSync();

    // google_fonts fetches from fonts.gstatic.com at runtime; the redesign
    // ships Plus Jakarta Sans as asset fonts instead.
    final runtimeFontUsers = Directory('lib')
        .listSync(recursive: true)
        .whereType<File>()
        .where((f) => f.path.endsWith('.dart'))
        .where((f) {
          final src = f.readAsStringSync();
          return src.contains('package:google_fonts/') ||
              src.contains('GoogleFonts.');
        })
        .map((f) => f.path)
        .toList();
    expect(runtimeFontUsers, isEmpty,
        reason: 'Runtime font fetching makes the first paint depend on '
            'fonts.gstatic.com; bundle the font in pubspec.yaml instead.');

    expect(pubspec, contains('- family: ${AppTypography.fontFamily}'));
    final assets = RegExp(r'- asset: (assets/fonts/plus_jakarta_sans/\S+\.ttf)')
        .allMatches(pubspec)
        .map((m) => m.group(1)!)
        .toList();
    expect(assets, hasLength(greaterThanOrEqualTo(5)),
        reason: 'weights 400, 500, 600, 700 and 800 are bundled');
    for (final asset in assets) {
      expect(File(asset).existsSync(), isTrue, reason: '$asset is missing');
    }
  });

  test('first frame is painted before any network-bound start-up', () {
    // Normalised so the '\n}\n' lookup below also works in CRLF checkouts
    // (Windows with core.autocrlf=true).
    final src =
        File('lib/main.dart').readAsStringSync().replaceAll('\r\n', '\n');

    // main() itself must not await Firebase/AdMob: on web initializeApp
    // downloads the Firebase JS SDK from www.gstatic.com, and awaiting it
    // before runApp left a blank page (forever when a blocker drops a module).
    final mainStart = src.indexOf('Future<void> main() async {');
    expect(mainStart, isNonNegative);
    final mainBody = src.substring(mainStart, src.indexOf('\n}\n', mainStart));
    expect(mainBody, contains('runApp(_BootstrapApp(platformInit: _initPlatformServices()))'));
    expect(mainBody, isNot(contains('await Firebase')));
    expect(mainBody, isNot(contains('await AdService')));
    expect(mainBody, isNot(contains('await _initPlatformServices')));

    // ...but the app services are still created only after it finished.
    final initBody = src.substring(src.indexOf('Future<void> _init() async {'));
    final awaitPlatform = initBody.indexOf('await widget.platformInit;');
    expect(awaitPlatform, isNonNegative);
    expect(initBody.indexOf('await AppServices.create()'),
        greaterThan(awaitPlatform));
  });

  test('bootstrap offline screen uses the bundled-font theme', () {
    final src = File('lib/main.dart').readAsStringSync();
    final offline = src.substring(
        src.lastIndexOf('MaterialApp(', src.indexOf('OfflineView(onRetry')),
        src.indexOf('OfflineView(onRetry'));
    expect(offline, contains('theme: AppTheme.light()'));
    expect(offline, contains('darkTheme: AppTheme.dark()'));
  });

  testWidgets('offline screen text resolves to the bundled font',
      (tester) async {
    await tester.pumpWidget(MaterialApp(
      theme: AppTheme.light(),
      home: OfflineView(onRetry: () {}),
    ));

    for (final text in [
      'İnternet Bağlantısı Yok',
      'Karar vermek için internete bağlı olmalısın. Lütfen bağlantını kontrol et.',
    ]) {
      final paragraph = tester.renderObject<RenderParagraph>(find.text(text));
      expect(paragraph.text.style?.fontFamily, AppTypography.fontFamily,
          reason: '"$text" must not fall back to the network-loaded Roboto');
    }
  });
}
