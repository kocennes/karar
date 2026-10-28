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

  test('capsule content meets AA in both themes over any backdrop', () {
    // The dark capsule is slightly translucent, so measure it composited
    // over the extremes a photo or gradient can put behind it.
    const backdrops = [Color(0xFFFFFFFF), Color(0xFF000000)];
    for (final k in [l, d]) {
      for (final backdrop in [...backdrops, k.canvas]) {
        final capsule = Color.alphaBlend(k.capsule, backdrop);
        _expectRatio(k.onCapsuleText, capsule, 4.5);
        _expectRatio(k.onCapsuleMuted, capsule, 4.5);
        _expectRatio(k.onCapsuleHakli, capsule, 4.5);
        _expectRatio(k.onCapsuleHaksiz, capsule, 4.5);
        // Icons and verdict-chip bar segments are UI graphics (>= 3:1).
        _expectRatio(k.onCapsuleHakliIcon, capsule, 3);
        _expectRatio(k.onCapsuleHaksizIcon, capsule, 3);
        _expectRatio(k.onCapsuleVoteIcon(true), capsule, 3);
        _expectRatio(k.onCapsuleVoteIcon(false), capsule, 3);
        _expectRatio(k.hakliBar, capsule, 3);
        _expectRatio(k.haksizBar, capsule, 3);
        // Saved bookmark on the solid rail circle.
        _expectRatio(k.primary, capsule, 3);
      }
      // Selected vote capsule: the ring separates the vote fill (>= 3:1).
      _expectRatio(k.capsuleSelectedRing, k.hakliFill, 3);
      _expectRatio(k.capsuleSelectedRing, k.haksizFill, 3);
    }
  });

  test('light capsule tokens are unchanged', () {
    expect(l.capsule, const Color(0xFFFFFFFF));
    expect(l.onCapsuleText, const Color(0xFF15131F));
    expect(l.onCapsuleMuted, const Color(0xFF5B576D));
    expect(l.onCapsuleHakli, const Color(0xFF116B34));
    expect(l.onCapsuleHaksiz, const Color(0xFFB4232A));
    expect(l.onCapsuleHakliIcon, l.hakliFill);
    expect(l.onCapsuleHaksizIcon, l.haksizFill);
    expect(l.capsuleSelectedRing, l.capsule);
    expect(l.capsuleSide, isNull);
    expect(l.capsuleBorder, isNull);
    expect(KararShadow.capsuleOf(Brightness.light), KararShadow.capsule);
    expect(
      KararShadow.capsuleSelectedOf(Brightness.light),
      KararShadow.capsuleSelected,
    );
  });

  test('dark capsules are dark tonal with a hairline, not glare blocks', () {
    // Opaque enough that content contrast holds over any photo.
    expect(d.capsule.a, greaterThanOrEqualTo(0.9));
    // Dark tone: close to the dark surfaces, far from the light text.
    final onCanvas = Color.alphaBlend(d.capsule, d.canvas);
    expect(_contrastRatio(onCanvas, d.canvas), lessThan(1.5));
    _expectRatio(d.text1, onCanvas, 10);
    // Light content on it.
    expect(d.onCapsuleText, d.text1);
    expect(d.onCapsuleMuted, d.text2);
    expect(d.onCapsuleHakli, d.hakliText);
    expect(d.onCapsuleHaksiz, d.haksizText);
    // Hairline edge instead of a (invisible) shadow.
    expect(d.capsuleSide?.color, d.hairline);
    expect(d.capsuleBorder, isNotNull);
    expect(KararShadow.capsuleOf(Brightness.dark), isEmpty);
    expect(KararShadow.capsuleSelectedOf(Brightness.dark), isEmpty);
  });

  test('ink on the streak gradient meets AA in both themes', () {
    for (final stop in KararGradients.streak.colors) {
      _expectRatio(KararGradients.onStreak, stop, 4.5);
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
