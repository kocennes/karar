import 'package:flutter/material.dart';

import '../../../core/layout/breakpoints.dart';
import '../../../core/theme/app_typography.dart';
import '../../../core/theme/karar_tokens.dart';
import '../../../core/utils/count_format.dart';
import '../../../shared/models/post.dart';
import '../../../shared/widgets/post_surface.dart';
import 'count_pill.dart';

/// Profile grid tile (spec §6.5 `VerdictTile`): a small post surface
/// (category gradient or photo, radius 20, no quote watermark) with the
/// title at the top-left and a [CountPill] with the verdict at the bottom.
///
/// Lives in the profile feature (used by the profile tabs, my posts, saved
/// posts and other profiles).
/// TODO(owner D/shared): promote to lib/shared/widgets/verdict_tile.dart if
/// another area needs it.
class VerdictTile extends StatelessWidget {
  const VerdictTile({
    super.key,
    required this.post,
    required this.onTap,
    this.trailingAction,
    this.footer,
  });

  final Post post;
  final VoidCallback onTap;

  /// Optional 44 × 44 control in the top-right corner (owner "…" menu).
  final Widget? trailingAction;

  /// Replaces the bottom [CountPill] (e.g. a moderation status panel).
  final Widget? footer;

  /// Extra top scrim for photo tiles: the title sits at the top of the tile,
  /// so it needs more than the card's 26 % top scrim to stay readable.
  static const _photoTitleScrim = LinearGradient(
    begin: Alignment.topCenter,
    end: Alignment.bottomCenter,
    colors: [Color(0x73000000), Color(0x00000000)],
    stops: [0, 0.62],
  );

  /// Grid delegate for verdict tiles: 2 columns below 600 px (aspect 0.8),
  /// 3 columns from 600 px (aspect 0.72), 8 px gaps.
  static SliverGridDelegate gridDelegate(BuildContext context) {
    final wide = context.screenWidth >= Breakpoints.mobile;
    return SliverGridDelegateWithFixedCrossAxisCount(
      crossAxisCount: wide ? 3 : 2,
      mainAxisSpacing: 8,
      crossAxisSpacing: 8,
      childAspectRatio: wide ? 0.72 : 0.8,
    );
  }

  /// The verdict read-out shown in the pill: the winning side with its
  /// percentage once `showPercentage` (≥ 40 votes), else the vote count.
  static ({String label, IconData? icon, bool? hakli}) verdictOf(Post post) {
    if (!post.showPercentage) {
      return (
        label: '${formatCompactCount(post.totalVotes)} oy',
        icon: null,
        hakli: null,
      );
    }
    final p = post.hakliPercent;
    if (p >= 50) {
      return (label: '${formatPercent(p)} Haklı', icon: Icons.check_rounded, hakli: true);
    }
    return (
      label: '${formatPercent(100 - p)} Haksız',
      icon: Icons.close_rounded,
      hakli: false,
    );
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final verdict = verdictOf(post);
    final isPhoto = PostSurface.isPhotoPost(post);
    final radius = KararRadius.all(KararRadius.tile);

    final content = Padding(
      padding: EdgeInsets.fromLTRB(14, 14, trailingAction != null ? 40 : 14, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Expanded(
            child: ClipRect(
              child: Align(
                alignment: AlignmentDirectional.topStart,
                child: Text(
                  post.title,
                  maxLines: 5,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.labelS.copyWith(
                    fontSize: 14,
                    height: 1.3,
                    fontWeight: FontWeight.w800,
                    color: Colors.white,
                  ),
                ),
              ),
            ),
          ),
          const SizedBox(height: 8),
          footer ??
              Center(
                child: CountPill(
                  label: verdict.label,
                  icon: verdict.icon,
                  iconColor: verdict.hakli == null
                      ? null
                      : (verdict.hakli! ? kc.onCapsuleHakli : kc.onCapsuleHaksiz),
                ),
              ),
        ],
      ),
    );

    return Semantics(
      container: true,
      explicitChildNodes: true,
      child: PostSurface(
        post: post,
        radius: KararRadius.tile,
        showQuote: false,
        child: Stack(
          fit: StackFit.expand,
          children: [
            if (isPhoto)
              const IgnorePointer(
                child: ExcludeSemantics(
                  child: DecoratedBox(
                    decoration: BoxDecoration(gradient: _photoTitleScrim),
                  ),
                ),
              ),
            Semantics(
              button: true,
              label: 'Gönderi: ${post.title}',
              value: verdict.label,
              excludeSemantics: footer == null,
              child: Material(
                type: MaterialType.transparency,
                child: InkWell(
                  onTap: onTap,
                  borderRadius: radius,
                  splashColor: Colors.white.withValues(alpha: 0.12),
                  highlightColor: Colors.white.withValues(alpha: 0.06),
                  child: content,
                ),
              ),
            ),
            if (trailingAction != null)
              Positioned(top: 0, right: 0, child: trailingAction!),
          ],
        ),
      ),
    );
  }
}

/// Bottom panel replacing the count pill when a post is not active
/// (under review, hidden, removed): black veil, white icon + label + reason.
class TileStatusPanel extends StatelessWidget {
  const TileStatusPanel({
    super.key,
    required this.icon,
    required this.label,
    this.reason,
  });

  final IconData icon;
  final String label;
  final String? reason;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(10, 8, 10, 8),
      decoration: BoxDecoration(
        color: KararGlass.chipOnPhoto,
        borderRadius: KararRadius.all(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            children: [
              Icon(icon, size: 14, color: Colors.white),
              const SizedBox(width: 6),
              Flexible(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.micro.copyWith(color: Colors.white),
                ),
              ),
            ],
          ),
          if (reason != null && reason!.isNotEmpty) ...[
            const SizedBox(height: 2),
            Text(
              reason!,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: AppTypography.meta.copyWith(
                color: Colors.white,
                fontSize: 11,
                height: 1.3,
              ),
            ),
          ],
        ],
      ),
    );
  }
}
