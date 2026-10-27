import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/count_format.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karar_pressable.dart';
import 'weekly_featured_provider.dart';
import 'widgets/feed_skeletons.dart';

/// "Bu Haftanın Kararı" hero (spec §5.8): an ink radial-gradient card in
/// both themes with a gold overline, a quoted title, a mini verdict bar,
/// the comment count and a "Görüntüle" pill.
class WeeklyFeaturedCard extends ConsumerWidget {
  const WeeklyFeaturedCard({
    super.key,
    this.margin = const EdgeInsets.fromLTRB(16, 4, 16, 0),
    this.dense = false,
  });

  /// Outer margin (the desktop right rail passes zero).
  final EdgeInsetsGeometry margin;

  /// Phone feed header: a one-line title and tighter padding, so the first
  /// post card's verdict clears the floating nav on ~844 pt screens.
  final bool dense;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(weeklyFeaturedProvider);

    return async.when(
      data: (post) => post == null
          ? const SizedBox.shrink()
          : Padding(
              padding: margin,
              child: _FeaturedCard(post: post, dense: dense),
            ),
      loading: () => Padding(
        padding: margin,
        child: _FeaturedCardSkeleton(dense: dense),
      ),
      error: (_, __) => const SizedBox.shrink(),
    );
  }
}

/// Shared hero container: radial ink gradient, radius 24, light shadow /
/// dark hairline.
class _HeroFrame extends StatelessWidget {
  const _HeroFrame({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final isDark = context.isDark;
    final radius = KararRadius.all(KararRadius.surface);
    return DecoratedBox(
      decoration: BoxDecoration(
        borderRadius: radius,
        boxShadow: isDark
            ? null
            : [
                BoxShadow(
                  color:
                      KararGradients.hero.colors.last.withValues(alpha: 0.22),
                  offset: const Offset(0, 10),
                  blurRadius: 24,
                  spreadRadius: -6,
                ),
              ],
      ),
      child: ClipRRect(
        borderRadius: radius,
        child: Material(
          type: MaterialType.transparency,
          child: Ink(
            decoration: BoxDecoration(
              gradient: KararGradients.hero,
              borderRadius: radius,
              border: isDark
                  ? Border.all(color: KararShadow.cardHairlineDark)
                  : null,
            ),
            child: child,
          ),
        ),
      ),
    );
  }
}

class _FeaturedCard extends StatelessWidget {
  const _FeaturedCard({required this.post, required this.dense});

  final Post post;
  final bool dense;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final hakliPct = post.hakliPercent;
    final showPct = post.showPercentage;
    void open() => context.push('/posts/${post.id}', extra: post);

    final white12 = AppTypography.meta.copyWith(
      fontWeight: FontWeight.w800,
      letterSpacing: 0,
      color: kc.onColor,
      fontFeatures: AppTypography.tabular,
    );

    return LayoutBuilder(
      builder: (context, constraints) {
        final textScale = MediaQuery.textScalerOf(context).scale(12) / 12;
        // Below 340 px (scaled with the text size) the pill becomes a
        // 36 px arrow circle so the row never overflows.
        final narrow =
            constraints.maxWidth < 340 * textScale.clamp(1.0, 2.0);

        // The button's 44 px hit box is taller than its 32/36 px visual, so
        // the paddings below are 6 px smaller than the visual gaps.
        final viewButton = narrow
            ? KararPressable(
                onTap: open,
                semanticLabel: 'Görüntüle',
                tooltip: 'Görüntüle',
                focusShape: BoxShape.circle,
                child: Container(
                  width: 36,
                  height: 36,
                  decoration: BoxDecoration(
                    color: kc.onColorTrack,
                    shape: BoxShape.circle,
                  ),
                  alignment: Alignment.center,
                  child: Icon(
                    Icons.arrow_forward_rounded,
                    size: 18,
                    color: kc.onColor,
                  ),
                ),
              )
            : KararPressable(
                onTap: open,
                semanticLabel: 'Görüntüle',
                focusRadius: 16,
                child: Container(
                  constraints: const BoxConstraints(minHeight: 32),
                  padding: const EdgeInsets.symmetric(horizontal: 12),
                  decoration: BoxDecoration(
                    color: kc.onColorTrack,
                    borderRadius: KararRadius.all(16),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text('Görüntüle', style: white12),
                      const SizedBox(width: 4),
                      Icon(
                        Icons.arrow_forward_rounded,
                        size: 15,
                        color: kc.onColor,
                      ),
                    ],
                  ),
                ),
              );

        return _HeroFrame(
          child: InkWell(
            onTap: open,
            splashColor: kc.splashOnColor,
            highlightColor: kc.highlightOnColor,
            child: Padding(
              padding: dense
                  ? const EdgeInsets.fromLTRB(16, 12, 12, 6)
                  : const EdgeInsets.fromLTRB(16, 14, 14, 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Row(
                    children: [
                      const ExcludeSemantics(
                        child: Text('🏆', style: TextStyle(fontSize: 14)),
                      ),
                      const SizedBox(width: 6),
                      Flexible(
                        child: Text(
                          'Bu Haftanın Kararı',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: AppTypography.meta.copyWith(
                            fontWeight: FontWeight.w800,
                            letterSpacing: 0.2,
                            color: kc.gold,
                          ),
                        ),
                      ),
                    ],
                  ),
                  SizedBox(height: dense ? 6 : 8),
                  Text(
                    '“${post.title}”',
                    maxLines: dense ? 1 : 2,
                    overflow: TextOverflow.ellipsis,
                    style: AppTypography.bodyStrong.copyWith(
                      fontSize: dense ? 15 : 16,
                      height: 1.3,
                      fontWeight: FontWeight.w800,
                      color: kc.onColor,
                    ),
                  ),
                  SizedBox(height: dense ? 4 : 6),
                  Row(
                    children: [
                      Expanded(
                        child: Row(
                          children: [
                            if (showPct) ...[
                              Flexible(
                                child: ConstrainedBox(
                                  constraints:
                                      const BoxConstraints(maxWidth: 84),
                                  child: _MiniBar(hakliPercent: hakliPct),
                                ),
                              ),
                              const SizedBox(width: 8),
                              Text('%$hakliPct Haklı', style: white12),
                              const SizedBox(width: 12),
                            ],
                            ExcludeSemantics(
                              child: Icon(
                                Icons.chat_bubble_outline_rounded,
                                size: 15,
                                color: kc.onColor,
                              ),
                            ),
                            const SizedBox(width: 4),
                            Text(
                              formatCompactCount(post.commentCount),
                              style: white12,
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(width: 8),
                      viewButton,
                    ],
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }
}

/// 84 × 6 Haklı/Haksız mini bar for the ink background (#4ADE80 / #FF7A7A,
/// 2 px gap). The row stretches its segments to the 6 px height (a loose
/// cross axis would lay the empty boxes out at zero height).
class _MiniBar extends StatelessWidget {
  const _MiniBar({required this.hakliPercent});

  final int hakliPercent;

  @override
  Widget build(BuildContext context) {
    final hakli = hakliPercent.clamp(0, 100);
    return ExcludeSemantics(
      child: SizedBox(
        height: 6,
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (hakli > 0)
              Expanded(
                flex: hakli,
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    color: KararGradients.hakliOnInk,
                    borderRadius: KararRadius.all(3),
                  ),
                ),
              ),
            if (hakli > 0 && hakli < 100) const SizedBox(width: 2),
            if (hakli < 100)
              Expanded(
                flex: 100 - hakli,
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    color: KararGradients.haksizOnInk,
                    borderRadius: KararRadius.all(3),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _FeaturedCardSkeleton extends StatelessWidget {
  const _FeaturedCardSkeleton({required this.dense});

  final bool dense;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final bar = kc.onColor;
    return ExcludeSemantics(
      child: _HeroFrame(
        child: Padding(
          padding: dense
              ? const EdgeInsets.fromLTRB(16, 12, 12, 12)
              : const EdgeInsets.fromLTRB(16, 16, 14, 16),
          child: KararShimmer(
            baseColor: kc.highlightOnColor,
            highlightColor: kc.onColorTrack,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _bar(bar, width: 130, height: 12),
                SizedBox(height: dense ? 10 : 12),
                _bar(bar, height: 16),
                if (!dense) ...[
                  const SizedBox(height: 8),
                  _bar(bar, width: 200, height: 16),
                ],
                SizedBox(height: dense ? 12 : 16),
                Row(
                  children: [
                    _bar(bar, width: 84, height: 6),
                    const Spacer(),
                    _bar(bar, width: 96, height: 32, radius: 16),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  static Widget _bar(
    Color color, {
    double? width,
    required double height,
    double? radius,
  }) {
    return Container(
      width: width ?? double.infinity,
      height: height,
      decoration: BoxDecoration(
        color: color,
        borderRadius: KararRadius.all(radius ?? 4),
      ),
    );
  }
}
