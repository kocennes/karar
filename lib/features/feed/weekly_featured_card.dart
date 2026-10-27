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
/// both themes with a gold overline, a 2-line quoted title, a mini verdict
/// bar, the comment count and a "Görüntüle" pill.
class WeeklyFeaturedCard extends ConsumerWidget {
  const WeeklyFeaturedCard({
    super.key,
    this.margin = const EdgeInsets.fromLTRB(16, 4, 16, 0),
  });

  /// Outer margin (the desktop right rail passes zero).
  final EdgeInsetsGeometry margin;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(weeklyFeaturedProvider);

    return async.when(
      data: (post) => post == null
          ? const SizedBox.shrink()
          : Padding(padding: margin, child: _FeaturedCard(post: post)),
      loading: () =>
          Padding(padding: margin, child: const _FeaturedCardSkeleton()),
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
                      KararGradients.hero.colors.last.withValues(alpha: 0.25),
                  offset: const Offset(0, 12),
                  blurRadius: 28,
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
  const _FeaturedCard({required this.post});

  final Post post;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final hakliPct = post.hakliPercent;
    final showPct = post.showPercentage;
    void open() => context.push('/posts/${post.id}', extra: post);

    final white12 = AppTypography.meta.copyWith(
      fontWeight: FontWeight.w800,
      letterSpacing: 0,
      color: Colors.white,
      fontFeatures: AppTypography.tabular,
    );

    return LayoutBuilder(
      builder: (context, constraints) {
        final textScale = MediaQuery.textScalerOf(context).scale(12) / 12;
        // Below 340 px (scaled with the text size) the pill becomes a
        // 36 px arrow circle so the row never overflows.
        final narrow =
            constraints.maxWidth < 340 * textScale.clamp(1.0, 2.0);

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
                    color: Colors.white.withValues(alpha: 0.14),
                    shape: BoxShape.circle,
                  ),
                  alignment: Alignment.center,
                  child: const Icon(
                    Icons.arrow_forward_rounded,
                    size: 18,
                    color: Colors.white,
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
                    color: Colors.white.withValues(alpha: 0.14),
                    borderRadius: KararRadius.all(16),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text('Görüntüle', style: white12),
                      const SizedBox(width: 4),
                      const Icon(
                        Icons.arrow_forward_rounded,
                        size: 15,
                        color: Colors.white,
                      ),
                    ],
                  ),
                ),
              );

        return _HeroFrame(
          child: InkWell(
            onTap: open,
            splashColor: Colors.white.withValues(alpha: 0.10),
            highlightColor: Colors.white.withValues(alpha: 0.05),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 14, 14, 14),
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
                  const SizedBox(height: 8),
                  Text(
                    '“${post.title}”',
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: AppTypography.bodyStrong.copyWith(
                      fontSize: 16,
                      height: 1.3,
                      fontWeight: FontWeight.w800,
                      color: Colors.white,
                    ),
                  ),
                  const SizedBox(height: 12),
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
                            const ExcludeSemantics(
                              child: Icon(
                                Icons.chat_bubble_outline_rounded,
                                size: 15,
                                color: Colors.white,
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
/// 2 px gap).
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
  const _FeaturedCardSkeleton();

  @override
  Widget build(BuildContext context) {
    return ExcludeSemantics(
      child: _HeroFrame(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 16, 14, 16),
          child: KararShimmer(
            baseColor: Colors.white.withValues(alpha: 0.08),
            highlightColor: Colors.white.withValues(alpha: 0.16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _bar(width: 130, height: 12),
                const SizedBox(height: 12),
                _bar(height: 16),
                const SizedBox(height: 8),
                _bar(width: 200, height: 16),
                const SizedBox(height: 16),
                Row(
                  children: [
                    _bar(width: 84, height: 6),
                    const Spacer(),
                    _bar(width: 96, height: 32, radius: 16),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  static Widget _bar({double? width, required double height, double? radius}) {
    return Container(
      width: width ?? double.infinity,
      height: height,
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: KararRadius.all(radius ?? 4),
      ),
    );
  }
}
