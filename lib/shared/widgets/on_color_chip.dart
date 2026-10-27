import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import 'karar_pressable.dart';

/// Small chip on colour (gradient cards, photos) — spec §4.3 / §2.4.
///
/// h 30 (hit ≥ 44 when [onTap] is set), radius 15, padding L9 R11,
/// black 22 % fill (28 % + blur σ 10 when [blur]), white 16 px icon and
/// 12/800 white label (≥ 6.9:1 on every palette). High contrast: solid ink.
class OnColorChip extends StatelessWidget {
  const OnColorChip({
    super.key,
    required this.label,
    this.icon,
    this.blur = false,
    this.dotColor,
    this.onTap,
    this.semanticLabel,
    this.maxWidth,
  });

  final String label;
  final IconData? icon;

  /// Use on photos: darker fill + backdrop blur.
  final bool blur;

  /// Optional decorative dot before the label (e.g. gold for "AI destekli").
  final Color? dotColor;
  final VoidCallback? onTap;
  final String? semanticLabel;

  /// Optional cap for the label width (ellipsis).
  final double? maxWidth;

  @override
  Widget build(BuildContext context) {
    final highContrast = MediaQuery.maybeHighContrastOf(context) ?? false;
    final useBlur = blur && !highContrast;
    final fill = highContrast
        ? KararGlass.chipHighContrast
        : blur
            ? KararGlass.chipOnPhoto
            : KararGlass.chipOnColor;

    Widget text = Text(
      label,
      maxLines: 1,
      overflow: TextOverflow.ellipsis,
      style: AppTypography.meta.copyWith(
        color: Colors.white,
        fontWeight: FontWeight.w800,
        letterSpacing: 0,
      ),
    );
    if (maxWidth != null) {
      text = ConstrainedBox(
        constraints: BoxConstraints(maxWidth: maxWidth!),
        child: text,
      );
    } else {
      text = Flexible(child: text);
    }

    Widget chip = Container(
      constraints: const BoxConstraints(minHeight: 30),
      padding: const EdgeInsets.fromLTRB(9, 5, 11, 5),
      decoration: BoxDecoration(
        color: useBlur ? null : fill,
        borderRadius: KararRadius.all(15),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (dotColor != null) ...[
            ExcludeSemantics(
              child: Container(
                width: 7,
                height: 7,
                decoration:
                    BoxDecoration(color: dotColor, shape: BoxShape.circle),
              ),
            ),
            const SizedBox(width: 6),
          ],
          if (icon != null) ...[
            Icon(icon, size: 16, color: Colors.white),
            const SizedBox(width: 5),
          ],
          text,
        ],
      ),
    );

    if (useBlur) {
      chip = ClipRRect(
        borderRadius: KararRadius.all(15),
        child: BackdropFilter.grouped(
          filter: ImageFilter.blur(
            sigmaX: KararGlass.chipBlur,
            sigmaY: KararGlass.chipBlur,
          ),
          child: ColoredBox(color: fill, child: chip),
        ),
      );
    }

    if (onTap == null) {
      return Semantics(
        container: true,
        label: semanticLabel,
        child: chip,
      );
    }
    return KararPressable(
      onTap: onTap,
      semanticLabel: semanticLabel ?? label,
      focusRadius: 15,
      child: chip,
    );
  }
}
