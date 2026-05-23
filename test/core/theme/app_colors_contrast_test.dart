import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/theme/app_colors.dart';

void main() {
  test('light theme text colors meet WCAG AA contrast', () {
    _expectAa(AppColors.textPrimary, AppColors.surface);
    _expectAa(AppColors.textSecondary, AppColors.surface);
    _expectAa(AppColors.textTertiary, AppColors.surface);
    _expectAa(AppColors.textSecondary, AppColors.background);
    _expectAa(AppColors.textTertiary, AppColors.background);
  });

  test('dark theme text colors meet WCAG AA contrast', () {
    _expectAa(AppColors.darkTextPrimary, AppColors.darkBackground);
    _expectAa(AppColors.darkTextSecondary, AppColors.darkSurface);
    _expectAa(AppColors.darkTextTertiary, AppColors.darkSurface);
    _expectAa(AppColors.darkTextTertiary, AppColors.darkSurfaceVariant);
  });

  test('interactive colors meet WCAG AA with white foreground', () {
    _expectAa(Colors.white, AppColors.primary);
    _expectAa(Colors.white, AppColors.accent);
    _expectAa(Colors.white, AppColors.hakli);
    _expectAa(Colors.white, AppColors.haksiz);
    _expectAa(Colors.white, AppColors.error);
    _expectAa(Colors.white, AppColors.success);
  });
}

void _expectAa(Color foreground, Color background) {
  final ratio = _contrastRatio(foreground, background);
  expect(
    ratio,
    greaterThanOrEqualTo(4.5),
    reason:
        '${_hex(foreground)} on ${_hex(background)} contrast is ${ratio.toStringAsFixed(2)}',
  );
}

double _contrastRatio(Color a, Color b) {
  final lighter = a.computeLuminance() > b.computeLuminance() ? a : b;
  final darker = identical(lighter, a) ? b : a;
  return (lighter.computeLuminance() + 0.05) /
      (darker.computeLuminance() + 0.05);
}

String _hex(Color color) =>
    '#${color.toARGB32().toRadixString(16).padLeft(8, '0').substring(2)}';
