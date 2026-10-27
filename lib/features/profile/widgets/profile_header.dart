import 'package:flutter/material.dart';

import '../../../core/auth/auth_service.dart';
import '../../../core/theme/app_typography.dart';
import '../../../core/theme/karar_tokens.dart';
import '../../../shared/widgets/karar_avatar.dart';
import '../../../shared/widgets/karar_pressable.dart';
import '../../../shared/widgets/karar_sheet.dart';
import '../../../shared/widgets/karma_badge.dart';

/// One value/label pair of the profile stats row.
class ProfileStat {
  const ProfileStat({required this.value, required this.label, this.onTap});

  final String value;
  final String label;
  final VoidCallback? onTap;
}

/// Reference-style centred profile header (spec §6.5): ringed avatar,
/// "@handle" + karma badge, bio, meta line, stats row and an optional
/// actions row. Shared by the own profile and other users' profiles.
class ProfileHeader extends StatelessWidget {
  const ProfileHeader({
    super.key,
    required this.user,
    required this.stats,
    this.metaLine,
    this.actions,
  });

  final AuthUser user;
  final List<ProfileStat> stats;

  /// e.g. "14.03.2025 12:30 tarihinden beri üye · E-posta".
  final String? metaLine;

  /// Buttons under the stats row ("Profili Düzenle" + weekly circle).
  final Widget? actions;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final bio = (user.bio ?? '').trim();

    return Padding(
      padding: const EdgeInsets.fromLTRB(24, 8, 24, 0),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          KararAvatar(
            username: user.username,
            radius: 52,
            fontSize: 42,
            ring: KararAvatarRing.gradient,
          ),
          const SizedBox(height: 12),
          Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Flexible(
                child: Text(
                  '@${user.username}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.headline.copyWith(
                    fontSize: 22,
                    height: 28 / 22,
                    color: kc.text1,
                  ),
                ),
              ),
              const SizedBox(width: 2),
              KarmaBadgeButton(karma: user.karma, size: 20),
            ],
          ),
          if (bio.isNotEmpty) ...[
            const SizedBox(height: 6),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: Text(
                bio,
                maxLines: 3,
                overflow: TextOverflow.ellipsis,
                textAlign: TextAlign.center,
                style: AppTypography.bodyS.copyWith(color: kc.text2),
              ),
            ),
          ],
          if (metaLine != null) ...[
            const SizedBox(height: 6),
            Text(
              metaLine!,
              textAlign: TextAlign.center,
              style: AppTypography.meta.copyWith(color: kc.text3),
            ),
          ],
          if (stats.isNotEmpty) ...[
            const SizedBox(height: 18),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 4),
              child: Row(
                children: [
                  for (final stat in stats)
                    Expanded(child: _StatItem(stat: stat)),
                ],
              ),
            ),
          ],
          if (actions != null) ...[
            const SizedBox(height: 16),
            actions!,
          ],
        ],
      ),
    );
  }
}

class _StatItem extends StatelessWidget {
  const _StatItem({required this.stat});

  final ProfileStat stat;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final column = Padding(
      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 4),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            stat.value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: AppTypography.headline.copyWith(
              fontSize: 18,
              height: 24 / 18,
              color: kc.text1,
              fontFeatures: AppTypography.tabular,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            stat.label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: AppTypography.labelS.copyWith(
              fontWeight: FontWeight.w600,
              color: kc.text3,
            ),
          ),
        ],
      ),
    );

    if (stat.onTap == null) {
      return Semantics(
        container: true,
        label: '${stat.label}, ${stat.value}',
        excludeSemantics: true,
        child: column,
      );
    }
    return KararPressable(
      onTap: stat.onTap,
      semanticLabel: '${stat.label}, ${stat.value}',
      focusRadius: KararRadius.input,
      child: column,
    );
  }
}

/// Karma level emoji that opens the existing [KarmaBadgeSheet] in a Karar
/// sheet, with a ≥ 44 × 44 hit area (the shared badge's own detail tap is
/// only as big as the emoji).
class KarmaBadgeButton extends StatelessWidget {
  const KarmaBadgeButton({super.key, required this.karma, this.size = 16});

  final int karma;
  final double size;

  @override
  Widget build(BuildContext context) {
    return KararPressable(
      onTap: () => showKararSheet<void>(
        context,
        builder: (_) => KarmaBadgeSheet(karma: karma),
      ),
      semanticLabel: '${karmaBadgeLabel(karma)} rozeti — detay için dokun',
      focusShape: BoxShape.circle,
      child: KarmaBadge(karma: karma, size: size),
    );
  }
}
