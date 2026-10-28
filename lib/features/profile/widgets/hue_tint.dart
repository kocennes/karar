import 'package:flutter/material.dart';

import '../../../core/theme/karar_tokens.dart';

/// Tinted icon-tile pair (background + icon colour) for an accent hue that
/// has no token of its own (notification type tiles, weekly stat discs).
///
/// * Light: the hue itself on a 12 % tint of the surface (icon ≥ 4.2:1).
/// * Dark: a lighter tone of the same hue (HSL lightness 0.70, saturation
///   kept, so amber stays orange instead of turning beige as a lerp to
///   white did) on a 16 % tint of the dark surface (icon ≥ 5.8:1 for the
///   ocean, magenta and amber category hues).
///
/// TODO(owner A): replace with explicit KararColors tokens (spec §6.6 dark
/// pairs #F59E5B / #7FA8FF / #F27BB8 on their dark tints).
({Color bg, Color fg}) hueTint(KararColors kc, Color hue) {
  if (!kc.isDark) {
    return (
      bg: Color.alphaBlend(hue.withValues(alpha: 0.12), kc.surface),
      fg: hue,
    );
  }
  return (
    bg: Color.alphaBlend(hue.withValues(alpha: 0.16), kc.surface),
    fg: HSLColor.fromColor(hue).withLightness(0.70).toColor(),
  );
}
