import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:karar/features/legal/legal_screen.dart';

void main() {
  group('Legal asset contract', () {
    test('router loads every legal text from assets/legal', () {
      final paths = _legalAssetPaths();

      expect(paths, isNotEmpty);
      for (final path in paths) {
        expect(path, startsWith('assets/legal/'));
      }
    });

    test('every legal asset is declared in pubspec and committed', () {
      final pubspec = _read('pubspec.yaml');

      for (final path in _legalAssetPaths()) {
        expect(pubspec, contains('- $path'),
            reason: '$path must be declared under flutter.assets');
        expect(File(path).existsSync(), isTrue,
            reason: '$path must exist in a clean checkout');
        expect(_read(path).trim(), isNotEmpty);
      }
    });

    test('pubspec does not declare gitignored asset folders', () {
      final pubspec = _read('pubspec.yaml');

      // docs/ ve logo/ gitignore'da: temiz checkout'ta yoklar ve
      // flutter test / flutter build web asset bundle adımında kırılır.
      expect(pubspec, isNot(contains('- docs/')));
      expect(pubspec, isNot(contains('- logo/')));
    });

    testWidgets('LegalScreen renders every bundled legal text', (tester) async {
      for (final path in _legalAssetPaths()) {
        final heading = _read(path).split('\n').first.replaceFirst('# ', '');

        await tester.pumpWidget(MaterialApp(
          home: LegalScreen(title: 'Hukuki', assetPath: path),
        ));
        await tester.pumpAndSettle();

        expect(find.textContaining('Hata:'), findsNothing, reason: path);
        expect(find.textContaining(heading.trim()), findsOneWidget,
            reason: path);
      }
    });
  });
}

List<String> _legalAssetPaths() => RegExp(r"assetPath: '([^']+)'")
    .allMatches(_read('lib/core/router/app_router.dart'))
    .map((m) => m.group(1)!)
    .toList();

String _read(String path) => File(path).readAsStringSync();
