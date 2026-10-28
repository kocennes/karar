import 'package:flutter/material.dart';

import '../../../core/theme/app_typography.dart';
import '../../../core/theme/karar_tokens.dart';

/// Small capsule with an optional icon (spec §4.3 `CountPill`): padding
/// 4 × 10, radius 12, `micro` 11/800 in `onCapsuleText`. Light: white pill
/// + capsule shadow, #15131F text. Dark: tonal #24202F @ 92 % + white 8 %
/// hairline, #F4F3F8 text (no off-white glare block).
///
/// Lives in the profile feature because only profile/search tiles use it.
/// TODO(owner D/shared): promote to lib/shared/widgets/count_pill.dart when
/// another area needs it.
class CountPill extends StatelessWidget {
  const CountPill({
    super.key,
    required this.label,
    this.icon,
    this.iconColor,
  });

  final String label;
  final IconData? icon;

  /// Defaults to `onCapsuleText`. Use `onCapsuleHakli` / `onCapsuleHaksiz`
  /// for vote glyphs (≥ 4.97:1 on the capsule in both themes).
  final Color? iconColor;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: kc.capsule,
        borderRadius: KararRadius.all(12),
        border: kc.capsuleBorder,
        boxShadow: KararShadow.capsuleOf(Theme.of(context).brightness),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 14, color: iconColor ?? kc.onCapsuleText),
            const SizedBox(width: 3),
          ],
          Flexible(
            child: Text(
              label,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: AppTypography.micro.copyWith(
                color: kc.onCapsuleText,
                letterSpacing: 0,
                fontFeatures: AppTypography.tabular,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
