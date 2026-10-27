import 'package:flutter/material.dart';

/// Legacy static colour names, re-pointed to the "Yeni Tasarım" palette
/// (spec §2.2, light column) so existing call sites keep compiling.
///
/// New code should read theme-aware tokens via `context.kc`
/// (`KararColors`, see `karar_tokens.dart`) or `Theme.of(context).colorScheme`:
/// most of the `text*` names below are light-theme values and are wrong on
/// dark surfaces.
class AppColors {
  // Vote colours — fills (same in both themes; white text ≥ 4.7:1).
  static const hakli = Color(0xFF15803D);
  static const haksiz = Color(0xFFD6363C);

  /// Vote text colours on light surfaces (≥ 6.5:1 on white).
  static const hakliText = Color(0xFF116B34);
  static const haksizText = Color(0xFFB4232A);
  static const hakliDim = hakliText;
  static const haksizDim = haksizText;

  /// Vote soft (tonal) backgrounds, light theme.
  static const hakliSoft = Color(0xFFE4F5EA);
  static const haksizSoft = Color(0xFFFDE8E8);

  // Vote colours — dark mode TEXT on dark surfaces (never behind white text).
  static const darkHakli = Color(0xFF4ADE80);
  static const darkHaksiz = Color(0xFFFF7A7A);

  // Brand.
  /// Brand fill (#7D50F0). White on it 4.91:1. As small text on light
  /// surfaces use [primaryText].
  static const primary = Color(0xFF7D50F0);
  static const primaryText = Color(0xFF6438E0);
  static const primaryContainer = Color(0xFFEEE8FD);
  static const onPrimaryContainer = Color(0xFF4A22B5);
  static const darkPrimary = Color(0xFFA98BFF);
  static const accent = haksiz;

  // Light mode.
  static const background = Color(0xFFF5F4F9); // canvas
  static const surface = Color(0xFFFFFFFF);
  static const surfaceContainerHighest = Color(0xFFEFEDF5); // muted
  static const muted = surfaceContainerHighest;
  static const border = Color(0xFFE7E5EE); // outlineVariant (hairline)
  static const outline = Color(0xFF8E8AA3);
  static const textPrimary = Color(0xFF15131F);
  static const textSecondary = Color(0xFF5B576D);
  static const textTertiary = Color(0xFF67637A);
  static const ink = Color(0xFF15131F);

  // Dark mode — near-black violet surfaces.
  static const darkBackground = Color(0xFF0D0C12); // canvas
  static const darkSurface = Color(0xFF17151F);
  static const darkElevated = Color(0xFF1F1C2A);
  static const darkSurfaceContainerHighest = Color(0xFF262233); // muted
  static const darkBorder = Color(0xFF2F2B3D); // outlineVariant
  static const darkOutline = Color(0xFF77728F);
  static const darkTextPrimary = Color(0xFFF4F3F8);
  static const darkTextSecondary = Color(0xFFB6B2C6);
  static const darkTextTertiary = Color(0xFF958FAA);

  // Accents.
  static const gold = Color(0xFFF5C04E);
  static const goldText = Color(0xFF8A5A00);
  static const warnSoft = Color(0xFFFFF4D6);
  static const mint = Color(0xFF57CA8C); // decorative dot only

  // Status.
  static const success = hakli;
  static const warning = Color(0xFF92400E);
  static const error = haksiz;
}
