import 'dart:math' as math;
import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import '../../core/theme/karar_tokens.dart';
import 'karar_pressable.dart';

/// Visual styles of [KararCircleButton].
enum KararCircleStyle {
  /// Surface colour + soft shadow (dark: elevated + hairline).
  surface,

  /// Muted fill (chips/inputs colour), no shadow.
  muted,

  /// On gradient cards: black 22 % + white icon.
  onColor,

  /// On photos: black 28 % + backdrop blur σ 10 + white icon.
  glass,

  /// Ink fill (#15131F light / #F4F3F8 dark) with `onInk` icon.
  ink,

  /// Brand fill (`primaryFill`, both themes) with a white icon.
  primary,

  /// Light capsule (white / #F4F3F8) + capsule shadow, #15131F icon in
  /// both themes (rail circles).
  capsule,
}

/// Circular icon button (spec §4.3).
///
/// [size] is the visual diameter; the hit area is always ≥ 44 × 44, so
/// 32/36 px visuals stay accessible. Always has a [tooltip], which is also
/// the semantics label.
class KararCircleButton extends StatelessWidget {
  const KararCircleButton({
    super.key,
    required this.icon,
    required this.onPressed,
    required this.tooltip,
    this.style = KararCircleStyle.surface,
    this.size = 44,
    this.iconSize,
    this.iconColor,
    this.backgroundColor,
    this.selected = false,
    this.semanticLabel,
    this.badge,
  });

  final IconData icon;
  final VoidCallback? onPressed;
  final String tooltip;
  final KararCircleStyle style;

  /// Visual diameter.
  final double size;

  /// Defaults to 22 (scaled down for visuals below 40).
  final double? iconSize;
  final Color? iconColor;

  /// Overrides the style's fill.
  final Color? backgroundColor;

  /// Exposed to semantics (e.g. saved).
  final bool selected;

  /// Accessible name; defaults to [tooltip].
  final String? semanticLabel;

  /// Optional small overlay (e.g. an 8 px "filters active" dot), positioned
  /// top-right of the visual circle.
  final Widget? badge;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final brightness = Theme.of(context).brightness;
    final highContrast = MediaQuery.maybeHighContrastOf(context) ?? false;
    final enabled = onPressed != null;

    Color fill;
    Color fg;
    List<BoxShadow> shadows = const [];
    BorderSide side = BorderSide.none;
    var blur = false;

    switch (style) {
      case KararCircleStyle.surface:
        fill = kc.isDark ? kc.elevated : kc.surface;
        fg = kc.text1;
        shadows = KararShadow.soft(brightness);
        if (kc.isDark) side = BorderSide(color: kc.hairline);
      case KararCircleStyle.muted:
        fill = kc.muted;
        fg = kc.text1;
      case KararCircleStyle.onColor:
        fill = highContrast ? KararGlass.chipHighContrast : KararGlass.chipOnColor;
        fg = Colors.white;
      case KararCircleStyle.glass:
        fill = highContrast ? KararGlass.chipHighContrast : KararGlass.chipOnPhoto;
        fg = Colors.white;
        blur = !highContrast;
      case KararCircleStyle.ink:
        fill = kc.ink;
        fg = kc.onInk;
        shadows = KararShadow.float(brightness);
      case KararCircleStyle.primary:
        fill = kc.primaryFill;
        fg = Colors.white;
      case KararCircleStyle.capsule:
        fill = kc.capsule;
        fg = kc.onCapsuleText;
        shadows = KararShadow.capsule;
    }

    if (!enabled) {
      // Disabled: light styles keep their fill with a text3 icon; coloured
      // styles fade.
      switch (style) {
        case KararCircleStyle.surface:
        case KararCircleStyle.muted:
          fg = kc.text3;
        case KararCircleStyle.primary:
          fill = kc.muted;
          fg = kc.text3;
        case KararCircleStyle.capsule:
          fg = kc.onCapsuleMuted;
        case KararCircleStyle.onColor:
        case KararCircleStyle.glass:
        case KararCircleStyle.ink:
          break;
      }
    }

    fill = backgroundColor ?? fill;
    fg = (enabled ? iconColor : null) ?? fg;
    final effectiveIconSize = iconSize ?? (size < 40 ? math.min(20.0, size * 0.55) : 22.0);

    Widget circle = Container(
      width: size,
      height: size,
      decoration: ShapeDecoration(
        color: blur ? null : fill,
        shape: CircleBorder(side: side),
        shadows: shadows,
      ),
      alignment: Alignment.center,
      child: Icon(icon, size: effectiveIconSize, color: fg),
    );

    if (blur) {
      circle = ClipOval(
        child: BackdropFilter.grouped(
          filter: ImageFilter.blur(
            sigmaX: KararGlass.chipBlur,
            sigmaY: KararGlass.chipBlur,
          ),
          child: ColoredBox(color: fill, child: circle),
        ),
      );
    }

    if (!enabled &&
        (style == KararCircleStyle.onColor ||
            style == KararCircleStyle.glass ||
            style == KararCircleStyle.ink)) {
      circle = Opacity(opacity: 0.5, child: circle);
    }

    if (badge != null) {
      circle = Stack(
        clipBehavior: Clip.none,
        children: [
          circle,
          Positioned(top: size * 0.08, right: size * 0.08, child: badge!),
        ],
      );
    }

    return KararPressable(
      onTap: onPressed,
      tooltip: tooltip,
      semanticLabel: semanticLabel ?? tooltip,
      selected: selected ? true : null,
      focusShape: BoxShape.circle,
      pressedScale: 0.92,
      child: circle,
    );
  }
}
