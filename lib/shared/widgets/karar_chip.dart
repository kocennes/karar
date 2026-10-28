import 'package:flutter/material.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import 'karar_pressable.dart';

/// Colour tone of a [KararChip].
@immutable
class KararChipTone {
  const KararChipTone._(this.category);

  /// Unselected muted/text2, selected ink/onInk (near-black in light,
  /// brand violet in dark — never an off-white block).
  static const neutral = KararChipTone._(null);

  /// Selected = the category gradient + white label/icon.
  const KararChipTone.category(CategoryStyle this.category);

  final CategoryStyle? category;
}

/// Pill chip (spec §4.3): 38 px visual / 48 px hit, radius 999, H padding 14,
/// icon 16 + gap 6, `labelS`. Unselected muted/text2; selected ink/onInk
/// (or the category gradient for [KararChipTone.category]).
/// Press scale 0.96 + selection haptic.
///
/// [onCanvas]: the chip sits straight on the page canvas (feed category
/// row, notification controls, create-post rows, onboarding grid). In light
/// the unselected `muted` fill (#EFEDF5) is 1.06:1 against the canvas
/// (#F5F4F9) and the pills vanish, so they take the white `surface` fill on
/// [KararShadow.soft], like the reference's white chips on a tinted canvas.
/// Dark keeps `muted` (#262233 on #0D0C12 reads; dark uses no soft
/// shadows). A horizontal row of such chips needs `clipBehavior: Clip.none`
/// inside a [KararChipRowClip], or the shadow tail is cut off in a hard line
/// above and below the row.
class KararChip extends StatelessWidget {
  const KararChip({
    super.key,
    required this.label,
    this.icon,
    this.selected = false,
    this.onTap,
    this.onLongPress,
    this.trailing,
    this.tone = KararChipTone.neutral,
    this.semanticLabel,
    this.onCanvas = false,
  });

  final String label;
  final IconData? icon;
  final bool selected;
  final VoidCallback? onTap;
  final VoidCallback? onLongPress;

  /// Small trailing widget (★ followed marker, ✕ remove icon…). Its colour
  /// should come from `IconTheme`/`DefaultTextStyle`, which this chip sets.
  final Widget? trailing;
  final KararChipTone tone;
  final String? semanticLabel;

  /// White pill + soft shadow in light, for chips placed on the canvas.
  final bool onCanvas;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final category = tone.category;
    final brightness = Theme.of(context).brightness;
    final fg =
        selected ? (category != null ? kc.onColor : kc.onInk) : kc.text2;
    final decoration = BoxDecoration(
      color: selected
          ? (category != null ? null : kc.ink)
          : (onCanvas && !kc.isDark ? kc.surface : kc.muted),
      gradient: selected && category != null
          ? category.gradientFor(brightness: brightness)
          : null,
      borderRadius: KararRadius.all(KararRadius.pill),
      boxShadow: onCanvas ? KararShadow.soft(brightness) : null,
    );

    final content = AnimatedContainer(
      duration: KararMotion.d(context, KararMotion.fast),
      curve: KararMotion.standard,
      constraints: const BoxConstraints(minHeight: 38),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
      decoration: decoration,
      child: IconTheme.merge(
        data: IconThemeData(color: fg, size: 16),
        child: DefaultTextStyle.merge(
          style: AppTypography.labelS.copyWith(color: fg),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (icon != null) ...[
                Icon(icon, size: 16, color: fg),
                const SizedBox(width: 6),
              ],
              Flexible(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.labelS.copyWith(color: fg),
                ),
              ),
              if (trailing != null) ...[
                const SizedBox(width: 6),
                trailing!,
              ],
            ],
          ),
        ),
      ),
    );

    return KararPressable(
      onTap: onTap == null
          ? null
          : () {
              KararHaptics.toggle();
              onTap!();
            },
      onLongPress: onLongPress,
      selected: selected,
      semanticLabel: semanticLabel ?? label,
      focusRadius: 19,
      minSize: const Size(KararSpace.minTap, 48),
      child: content,
    );
  }
}

/// Clips a horizontally scrolling row of `KararChip(onCanvas: true)` at its
/// sides only.
///
/// Give the row's `ListView` / `SingleChildScrollView`
/// `clipBehavior: Clip.none`: chips scrolled past the ends are still cut at
/// the row's width (and at the centred column on wide screens), while the
/// chip shadows may spill above and below the row, where the neighbouring
/// sections paint over the faint tail.
class KararChipRowClip extends StatelessWidget {
  const KararChipRowClip({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return ClipRect(clipper: const _SidesOnlyClipper(), child: child);
  }
}

class _SidesOnlyClipper extends CustomClipper<Rect> {
  const _SidesOnlyClipper();

  /// How far the soft shadow may reach above / below the row (its tail is
  /// under 0.1 % alpha beyond this).
  static const double _bleed = 48;

  @override
  Rect getClip(Size size) =>
      Rect.fromLTRB(0, -_bleed, size.width, size.height + _bleed);

  @override
  bool shouldReclip(_SidesOnlyClipper oldClipper) => false;
}
