import 'package:flutter/material.dart';

import '../../../core/theme/app_typography.dart';
import '../../../core/theme/karar_tokens.dart';

/// Small light capsule with an optional icon (spec §4.3 `CountPill`):
/// white (dark #F4F3F8) pill, padding 4 × 10, radius 12, `micro` 11/800
/// #15131F, capsule shadow. Capsules are light in both themes, so the
/// text uses the theme-independent `onCapsule*` colours.
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
  /// for vote glyphs (≥ 5.5:1 on the capsule in both themes).
  final Color? iconColor;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: kc.capsule,
        borderRadius: KararRadius.all(12),
        boxShadow: KararShadow.capsule,
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
