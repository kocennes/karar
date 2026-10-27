import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/theme/app_theme.dart';
import 'package:karar/core/theme/category_style.dart';
import 'package:karar/core/theme/karar_tokens.dart';

void main() {
  const l = KararColors.light;
  const d = KararColors.dark;

  test('light text tokens meet WCAG AA on their surfaces', () {
    for (final bg in [l.surface, l.canvas]) {
      _expectRatio(l.text1, bg, 4.5);
      _expectRatio(l.text2, bg, 4.5);
      _expectRatio(l.text3, bg, 4.5);
      _expectRatio(l.primaryText, bg, 4.5);
      _expectRatio(l.hakliText, bg, 4.5);
      _expectRatio(l.haksizText, bg, 4.5);
    }
    _expectRatio(l.text3, l.muted, 4.5);
    _expectRatio(l.text2, l.muted, 4.5);
    _expectRatio(l.primaryText, l.primaryContainer, 4.5);
    _expectRatio(l.onPrimaryContainer, l.primaryContainer, 4.5);
    _expectRatio(l.hakliText, l.hakliSoft, 4.5);
    _expectRatio(l.haksizText, l.haksizSoft, 4.5);
    _expectRatio(l.warnText, l.warnSoft, 4.5);
    _expectRatio(l.goldText, l.warnSoft, 4.5);
    _expectRatio(l.onInk, l.ink, 4.5);
  });

  test('dark text tokens meet WCAG AA on their surfaces', () {
    for (final bg in [d.canvas, d.surface, d.elevated]) {
      _expectRatio(d.text1, bg, 4.5);
      _expectRatio(d.text2, bg, 4.5);
      _expectRatio(d.primaryText, bg, 4.5);
      _expectRatio(d.hakliText, bg, 4.5);
      _expectRatio(d.haksizText, bg, 4.5);
    }
    _expectRatio(d.text3, d.surface, 4.5);
    _expectRatio(d.text3, d.muted, 4.5);
    _expectRatio(d.onPrimaryContainer, d.primaryContainer, 4.5);
    _expectRatio(d.hakliText, d.hakliSoft, 4.5);
    _expectRatio(d.haksizText, d.haksizSoft, 4.5);
    _expectRatio(d.warnText, d.warnSoft, 4.5);
    _expectRatio(d.onInk, d.ink, 4.5);
  });

  test('white content on fills meets AA in both themes', () {
    for (final k in [l, d]) {
      _expectRatio(Colors.white, k.primaryFill, 4.5);
      _expectRatio(Colors.white, k.hakliFill, 4.5);
      _expectRatio(Colors.white, k.haksizFill, 4.5);
    }
  });

  test('capsule text is theme independent and AA on light capsules', () {
    for (final k in [l, d]) {
      _expectRatio(k.onCapsuleText, k.capsule, 4.5);
      _expectRatio(k.onCapsuleMuted, k.capsule, 4.5);
      _expectRatio(k.onCapsuleHakli, k.capsule, 4.5);
      _expectRatio(k.onCapsuleHaksiz, k.capsule, 4.5);
      // Icons on capsules are UI graphics (>= 3:1).
      _expectRatio(k.hakliFill, k.capsule, 3);
      _expectRatio(k.haksizFill, k.capsule, 3);
    }
  });

  test('UI boundaries reach 3:1', () {
    _expectRatio(l.outline, l.surface, 3);
    _expectRatio(d.outline, d.surface, 3);
  });

  test('colour scheme primary pairs with onPrimary in both themes', () {
    for (final theme in [AppTheme.light(), AppTheme.dark()]) {
      final cs = theme.colorScheme;
      _expectRatio(cs.onPrimary, cs.primary, 4.5);
      _expectRatio(cs.onSurface, cs.surface, 4.5);
      _expectRatio(cs.onSurfaceVariant, cs.surface, 4.5);
      _expectRatio(cs.onInverseSurface, cs.inverseSurface, 4.5);
      _expectRatio(cs.inversePrimary, cs.inverseSurface, 4.5);
      _expectRatio(cs.onErrorContainer, cs.errorContainer, 4.5);
      expect(theme.extension<KararColors>(), isNotNull);
    }
  });

  test('white text reaches AA on every category palette (light stop)', () {
    for (final id in [0, 1, 2, 3, 4, 5]) {
      final style = CategoryStyle.resolve(id: id);
      _expectRatio(Colors.white, style.start, 4.5);
      _expectRatio(Colors.white, style.seenGradient.colors.first, 4.5);
    }
  });

  test('category style resolves by id, then name, then cycle', () {
    expect(CategoryStyle.resolve(id: 2), CategoryStyle.magenta);
    expect(
      CategoryStyle.resolve(id: 99, name: 'İş Hayatı'),
      CategoryStyle.violet,
    );
    expect(
      CategoryStyle.resolve(id: 98, name: 'ARKADAŞLIK'),
      CategoryStyle.amber,
    );
    final unknown = CategoryStyle.resolve(id: 7, name: 'Bilinmeyen');
    expect(unknown.icon, CategoryStyle.unknown.icon);
  });
}

void _expectRatio(Color fg, Color bg, double min) {
  final ratio = _contrastRatio(fg, bg);
  expect(
    ratio,
    greaterThanOrEqualTo(min),
    reason: '${_hex(fg)} on ${_hex(bg)} is ${ratio.toStringAsFixed(2)}:1',
  );
}

double _contrastRatio(Color a, Color b) {
  final la = a.computeLuminance();
  final lb = b.computeLuminance();
  final hi = la > lb ? la : lb;
  final lo = la > lb ? lb : la;
  return (hi + 0.05) / (lo + 0.05);
}

String _hex(Color c) =>
    '#${c.toARGB32().toRadixString(16).padLeft(8, '0').substring(2)}';
