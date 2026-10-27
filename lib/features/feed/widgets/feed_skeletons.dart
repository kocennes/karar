import 'package:flutter/material.dart';
import 'package:shimmer/shimmer.dart';

import '../../../core/theme/karar_tokens.dart';
import '../../../core/theme/motion.dart';
import '../../../shared/widgets/vote_rail.dart';

/// Shimmer wrapper with the Karar skeleton colours (§5.4): base #ECEAF3 /
/// highlight #F8F7FB (dark #1F1C2A / #2A2638), period 1400 ms; static under
/// reduce motion.
class KararShimmer extends StatelessWidget {
  const KararShimmer({
    super.key,
    required this.child,
    this.baseColor,
    this.highlightColor,
  });

  final Widget child;
  final Color? baseColor;
  final Color? highlightColor;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Shimmer.fromColors(
      baseColor: baseColor ?? kc.shimmerBase,
      highlightColor: highlightColor ?? kc.shimmerHighlight,
      period: const Duration(milliseconds: 1400),
      enabled: !KararMotion.reduce(context),
      child: child,
    );
  }
}

/// A rounded placeholder block (painted by the surrounding [KararShimmer]).
class SkeletonBlock extends StatelessWidget {
  const SkeletonBlock({
    super.key,
    this.width,
    required this.height,
    this.radius = 8,
    this.circle = false,
  });

  final double? width;
  final double height;
  final double radius;
  final bool circle;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        color: context.kc.shimmerBase,
        shape: circle ? BoxShape.circle : BoxShape.rectangle,
        borderRadius: circle ? null : KararRadius.all(radius),
      ),
    );
  }
}

/// Feed card placeholder (§5.4): a radius-30 surface block (440 tall) with a
/// chip, two title bars and the verdict pill at the bottom-left, and the
/// rail's two capsules + three circles on the right.
class FeedCardSkeleton extends StatelessWidget {
  const FeedCardSkeleton({super.key, this.height = 440, this.compact = false});

  final double height;

  /// Desktop proportions (compact rail).
  final bool compact;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final brightness = Theme.of(context).brightness;
    final m = compact ? VoteRailMetrics.compact : VoteRailMetrics.card;
    final railInset = (m.width - m.voteCapsule.width) / 2;
    final hairline = KararShadow.hairlineFor(KararShadowKind.soft, brightness);

    return ExcludeSemantics(
      child: Container(
        height: height,
        decoration: BoxDecoration(
          color: kc.surface,
          borderRadius: KararRadius.all(KararRadius.card),
          boxShadow: KararShadow.soft(brightness),
          border: hairline == null ? null : Border.all(color: hairline),
        ),
        child: KararShimmer(
          child: Stack(
            children: [
              const Positioned(
                top: 18,
                left: 20,
                child: SkeletonBlock(width: 96, height: 30, radius: 15),
              ),
              Positioned(
                left: 20,
                right: m.width + 12,
                bottom: 18,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 220),
                      child: const SkeletonBlock(
                        width: double.infinity,
                        height: 22,
                        radius: 8,
                      ),
                    ),
                    const SizedBox(height: 10),
                    ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 160),
                      child: const SkeletonBlock(
                        width: double.infinity,
                        height: 22,
                        radius: 8,
                      ),
                    ),
                    const SizedBox(height: 22),
                    const SkeletonBlock(
                      width: double.infinity,
                      height: 34,
                      radius: 17,
                    ),
                  ],
                ),
              ),
              Positioned(
                top: 28 + 34,
                right: railInset,
                child: Column(
                  children: [
                    SkeletonBlock(
                      width: m.voteCapsule.width,
                      height: m.voteCapsule.height,
                      radius: m.voteCapsule.width / 2,
                    ),
                    SizedBox(height: m.gap),
                    SkeletonBlock(
                      width: m.voteCapsule.width,
                      height: m.voteCapsule.height,
                      radius: m.voteCapsule.width / 2,
                    ),
                    SizedBox(height: m.dividerBlock),
                    for (var i = 0; i < 3; i++) ...[
                      if (i > 0) SizedBox(height: m.gap),
                      SkeletonBlock(
                        width: m.circle,
                        height: m.circle,
                        circle: true,
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
