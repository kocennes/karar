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

  /// Unselected muted/text2, selected ink/onInk.
  static const neutral = KararChipTone._(null);

  /// Selected = the category gradient + white label/icon.
  const KararChipTone.category(CategoryStyle this.category);

  final CategoryStyle? category;
}

/// Pill chip (spec §4.3): 38 px visual / 48 px hit, radius 999, H padding 14,
/// icon 16 + gap 6, `labelS`. Unselected muted/text2; selected ink/onInk
/// (or the category gradient for [KararChipTone.category]).
/// Press scale 0.96 + selection haptic.
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

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final category = tone.category;
    final fg = selected ? (category != null ? Colors.white : kc.onInk) : kc.text2;
    final decoration = BoxDecoration(
      color: selected
          ? (category != null ? null : kc.ink)
          : kc.muted,
      gradient: selected && category != null ? category.gradient : null,
      borderRadius: KararRadius.all(KararRadius.pill),
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
