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

/// "Bu Haftanın Kararı" hero (spec §5.8): an ink radial-gradient card (dark:
/// a brighter violet core with a gold edge, see [_HeroFrame]) with a gold
/// overline, a quoted two-line title, a mini verdict bar, the comment count
/// and a "Görüntüle" pill (dense / narrow: an arrow disc).
class WeeklyFeaturedCard extends ConsumerWidget {
  const WeeklyFeaturedCard({
    super.key,
    this.margin = const EdgeInsets.fromLTRB(16, 4, 16, 0),
    this.dense = false,
  });

  /// Outer margin (the desktop right rail passes zero).
  final EdgeInsetsGeometry margin;

  /// Phone feed header: the two-line title sits beside a 40 px arrow disc
  /// (no separate "Görüntüle" row) with tighter padding, so the first post
  /// card's verdict still clears the floating nav on ~844 pt screens.
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

/// Shared hero container: radial ink gradient, radius 24.
///
/// * Light: the ink block with a soft ink shadow (it already stands out on
///   the pale canvas).
/// * Dark: on a near-black canvas the plain ink gradient read as one more
///   grey card (same family as the nav pill and the trend panel), so dark
///   gets its own hero treatment: a brighter violet core (brand fill
///   blended into the hero's top stop, white text ≥ 9:1, gold overline
///   ≥ 5.5:1), a gold hairline, a soft violet glow underneath and a faint
///   gold sheen behind the trophy.
class _HeroFrame extends StatelessWidget {
  const _HeroFrame({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final isDark = kc.isDark;
    final radius = KararRadius.all(KararRadius.surface);
    final heroBottom = KararGradients.hero.colors.last;

    final gradient = isDark ? KararGradients.heroDark : KararGradients.hero;

    final shadow = isDark
        ? BoxShadow(
            color: kc.primaryFill.withValues(alpha: 0.25),
            offset: const Offset(0, 10),
            blurRadius: 28,
            spreadRadius: -6,
          )
        : BoxShadow(
            color: heroBottom.withValues(alpha: 0.22),
            offset: const Offset(0, 10),
            blurRadius: 24,
            spreadRadius: -6,
          );

    return DecoratedBox(
      decoration: BoxDecoration(borderRadius: radius, boxShadow: [shadow]),
      child: ClipRRect(
        borderRadius: radius,
        child: Material(
          type: MaterialType.transparency,
          child: Ink(
            decoration: BoxDecoration(
              gradient: gradient,
              borderRadius: radius,
              border: isDark
                  ? Border.all(color: kc.gold.withValues(alpha: 0.26))
                  : null,
            ),
            child: isDark
                ? DecoratedBox(
                    decoration: BoxDecoration(
                      gradient: RadialGradient(
                        center: Alignment.topLeft,
                        radius: 0.75,
                        colors: [
                          kc.gold.withValues(alpha: 0.14),
                          kc.gold.withValues(alpha: 0),
                        ],
                      ),
                    ),
                    child: child,
                  )
                : child,
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

    // "🏆 Bu Haftanın Kararı" in gold.
    final overline = Row(
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
    );

    Widget title({required double fontSize, required double height}) => Text(
          '“${post.title}”',
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: AppTypography.bodyStrong.copyWith(
            fontSize: fontSize,
            height: height,
            fontWeight: FontWeight.w800,
            color: kc.onColor,
          ),
        );

    // Mini verdict bar + "%95 Haklı" + comment count.
    final stats = <Widget>[
      if (showPct) ...[
        Flexible(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 84),
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
      Text(formatCompactCount(post.commentCount), style: white12),
    ];

    Widget arrowButton(double size) => KararPressable(
          onTap: open,
          semanticLabel: 'Görüntüle',
          tooltip: 'Görüntüle',
          focusShape: BoxShape.circle,
          child: Container(
            width: size,
            height: size,
            decoration: BoxDecoration(
              color: kc.onColorTrack,
              shape: BoxShape.circle,
            ),
            alignment: Alignment.center,
            child: Icon(
              Icons.arrow_forward_rounded,
              size: size / 2,
              color: kc.onColor,
            ),
          ),
        );

    if (dense) {
      // Phone header: the full two-line title stays readable (spec §5.8)
      // while the card stays short — the "Görüntüle" action becomes an
      // arrow disc beside the text column instead of a third 44 px row.
      return _HeroFrame(
        child: InkWell(
          onTap: open,
          splashColor: kc.splashOnColor,
          highlightColor: kc.highlightOnColor,
          child: Padding(
            // Right: 12 + the disc's 2 px hit-box inset = 14 visual.
            padding: const EdgeInsets.fromLTRB(16, 12, 12, 12),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      overline,
                      const SizedBox(height: 6),
                      title(fontSize: 15, height: 1.25),
                      const SizedBox(height: 8),
                      Row(children: stats),
                    ],
                  ),
                ),
                const SizedBox(width: 4),
                arrowButton(40),
              ],
            ),
          ),
        ),
      );
    }

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
            ? arrowButton(36)
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
              padding: const EdgeInsets.fromLTRB(16, 14, 14, 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                mainAxisSize: MainAxisSize.min,
                children: [
                  overline,
                  const SizedBox(height: 8),
                  title(fontSize: 16, height: 1.3),
                  const SizedBox(height: 6),
                  Row(
                    children: [
                      Expanded(child: Row(children: stats)),
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
    // Mirrors the loaded layouts: dense = text column + arrow disc,
    // regular = overline, two title lines, stats row with the pill.
    final Widget bones = dense
        ? Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    _bar(bar, width: 130, height: 12),
                    const SizedBox(height: 10),
                    _bar(bar, height: 14),
                    const SizedBox(height: 6),
                    _bar(bar, width: 180, height: 14),
                    const SizedBox(height: 12),
                    _bar(bar, width: 84, height: 6),
                  ],
                ),
              ),
              const SizedBox(width: 12),
              _bar(bar, width: 40, height: 40, radius: 20),
            ],
          )
        : Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _bar(bar, width: 130, height: 12),
              const SizedBox(height: 12),
              _bar(bar, height: 16),
              const SizedBox(height: 8),
              _bar(bar, width: 200, height: 16),
              const SizedBox(height: 16),
              Row(
                children: [
                  _bar(bar, width: 84, height: 6),
                  const Spacer(),
                  _bar(bar, width: 96, height: 32, radius: 16),
                ],
              ),
            ],
          );
    return ExcludeSemantics(
      child: _HeroFrame(
        child: Padding(
          padding: dense
              ? const EdgeInsets.fromLTRB(16, 14, 14, 14)
              : const EdgeInsets.fromLTRB(16, 16, 14, 16),
          child: KararShimmer(
            baseColor: kc.highlightOnColor,
            highlightColor: kc.onColorTrack,
            child: bones,
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
