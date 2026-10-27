import 'dart:math' as math;

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

  /// Optional 48 × 48 control in the top-right corner (owner "…" menu)
  /// whose visual is a [menuDiameter] circle. Only the first title line
  /// makes room for it.
  final Widget? trailingAction;

  /// Replaces the bottom [CountPill] (e.g. a moderation status panel).
  final Widget? footer;

  /// Visual diameter of the [trailingAction] circle.
  static const double menuDiameter = 30;

  /// Space the first title line leaves for the [trailingAction] circle.
  static const double _menuInset = 28;

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
      return (
        label: '${formatPercent(p)} Haklı',
        icon: Icons.check_rounded,
        hakli: true
      );
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
    final titleStyle = AppTypography.labelS.copyWith(
      fontSize: 14,
      height: 1.3,
      fontWeight: FontWeight.w800,
      color: kc.onColor,
    );

    final content = Padding(
      padding: const EdgeInsets.fromLTRB(14, 14, 14, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Expanded(
            child: ClipRect(
              child: Align(
                alignment: AlignmentDirectional.topStart,
                child: _TileTitle(
                  text: post.title,
                  style: titleStyle,
                  firstLineInset: trailingAction != null ? _menuInset : 0,
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
                      : (verdict.hakli!
                          ? kc.onCapsuleHakli
                          : kc.onCapsuleHaksiz),
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
        showRailSheen: false,
        child: Stack(
          fit: StackFit.expand,
          children: [
            // Photo tiles: a light veil behind the title only. The card
            // already carries the §2.4 top/bottom scrims; the old 45 % extra
            // scrim turned most photos into a grey slab.
            if (isPhoto)
              IgnorePointer(
                child: ExcludeSemantics(
                  child: DecoratedBox(
                    decoration: BoxDecoration(
                      gradient: LinearGradient(
                        begin: Alignment.topCenter,
                        end: Alignment.bottomCenter,
                        colors: [
                          kc.photoVeil,
                          kc.photoVeil.withValues(alpha: 0),
                        ],
                        stops: const [0, 0.45],
                      ),
                    ),
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
                  splashColor: kc.splashOnColor,
                  highlightColor: kc.highlightOnColor,
                  child: content,
                ),
              ),
            ),
            if (trailingAction != null)
              Positioned(top: -2, right: -2, child: trailingAction!),
          ],
        ),
      ),
    );
  }
}

/// Tile title that fills the available height (max lines are computed from
/// the space above the pill) and leaves [firstLineInset] free only beside
/// the first line, so a corner button does not narrow the whole title.
class _TileTitle extends StatelessWidget {
  const _TileTitle({
    required this.text,
    required this.style,
    this.firstLineInset = 0,
  });

  final String text;
  final TextStyle style;
  final double firstLineInset;

  @override
  Widget build(BuildContext context) {
    final scaler = MediaQuery.textScalerOf(context);
    final direction = Directionality.of(context);
    // Measure with the same effective style the Text widgets render with.
    final effective = DefaultTextStyle.of(context).style.merge(style);

    return LayoutBuilder(
      builder: (context, constraints) {
        final probe = TextPainter(
          text: TextSpan(text: 'Ağ', style: effective),
          textDirection: direction,
          textScaler: scaler,
          maxLines: 1,
        )..layout();
        final lineHeight = math.max(1.0, probe.height);
        probe.dispose();

        final maxLines = constraints.maxHeight.isFinite
            ? math.max(1, (constraints.maxHeight / lineHeight).floor())
            : 5;

        Text plain(String value, int lines) => Text(
              value,
              maxLines: lines,
              overflow: TextOverflow.ellipsis,
              style: style,
            );

        final width = constraints.maxWidth;
        if (firstLineInset <= 0 || !width.isFinite || maxLines < 2) {
          return plain(text, maxLines);
        }

        // Break the first line at the narrower width, then let the rest
        // run at the full width.
        final first = TextPainter(
          text: TextSpan(text: text, style: effective),
          textDirection: direction,
          textScaler: scaler,
        )..layout(maxWidth: math.max(1.0, width - firstLineInset));
        final boundary = first.getLineBoundary(const TextPosition(offset: 0));
        final lineCount = first.computeLineMetrics().length;
        first.dispose();

        final head = text.substring(0, boundary.end).trimRight();
        final tail = text.substring(boundary.end).trimLeft();
        if (lineCount <= 1 || head.isEmpty || tail.isEmpty) {
          return Padding(
            padding: EdgeInsetsDirectional.only(end: firstLineInset),
            child: plain(text, maxLines),
          );
        }

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Padding(
              padding: EdgeInsetsDirectional.only(end: firstLineInset),
              child: Text(
                head,
                maxLines: 1,
                softWrap: false,
                overflow: TextOverflow.ellipsis,
                style: style,
              ),
            ),
            plain(tail, maxLines - 1),
          ],
        );
      },
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
    final onColor = context.kc.onColor;
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
              Icon(icon, size: 14, color: onColor),
              const SizedBox(width: 6),
              Flexible(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.micro.copyWith(color: onColor),
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
                color: onColor,
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
