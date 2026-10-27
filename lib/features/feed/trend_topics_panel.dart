import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/count_format.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karar_surface.dart';
import 'discover_provider.dart';
import 'widgets/feed_skeletons.dart';

/// "Trend Konular" panel (spec §5.9): a surface card with ranked rows.
class TrendTopicsPanel extends ConsumerWidget {
  const TrendTopicsPanel({
    super.key,
    this.margin = const EdgeInsets.fromLTRB(16, 0, 16, 8),
  });

  /// Outer margin (the desktop right rail passes zero).
  final EdgeInsetsGeometry margin;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(trendTopicsProvider);
    return async.when(
      data: (topics) => topics.isEmpty
          ? const SizedBox.shrink()
          : Padding(padding: margin, child: _TrendCard(topics: topics)),
      loading: () =>
          Padding(padding: margin, child: const _TrendCardSkeleton()),
      error: (_, __) => const SizedBox.shrink(),
    );
  }
}

class _TrendCard extends StatelessWidget {
  const _TrendCard({required this.topics});

  final List<TrendTopic> topics;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final shown = topics.take(5).toList();
    return KararSurface(
      radius: KararRadius.surface,
      padding: const EdgeInsets.fromLTRB(12, 18, 12, 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 4),
            child: Row(
              children: [
                const ExcludeSemantics(
                  child: Text('🔥', style: TextStyle(fontSize: 16)),
                ),
                const SizedBox(width: 8),
                Flexible(
                  child: Semantics(
                    header: true,
                    child: Text(
                      'Trend Konular',
                      style: AppTypography.bodyStrong.copyWith(
                        fontSize: 16,
                        fontWeight: FontWeight.w800,
                        color: kc.text1,
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 8),
          for (int i = 0; i < shown.length; i++)
            _TopicRow(rank: i + 1, topic: shown[i]),
        ],
      ),
    );
  }
}

class _TopicRow extends StatelessWidget {
  const _TopicRow({required this.rank, required this.topic});

  final int rank;
  final TrendTopic topic;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return InkWell(
      onTap: () =>
          context.push('/search?q=${Uri.encodeQueryComponent(topic.name)}'),
      borderRadius: KararRadius.all(12),
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 48),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 6),
          child: Row(
            children: [
              SizedBox(
                width: 22,
                child: Text(
                  '$rank',
                  style: AppTypography.bodyStrong.copyWith(
                    fontWeight: FontWeight.w800,
                    color: kc.text3,
                    fontFeatures: AppTypography.tabular,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '#${topic.name}',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: AppTypography.bodyStrong.copyWith(
                        color: kc.text1,
                      ),
                    ),
                    Text(
                      '${formatCompactCount(topic.postCount)} gönderi',
                      style: AppTypography.meta.copyWith(color: kc.text3),
                    ),
                  ],
                ),
              ),
              if (topic.growthPercent != null) ...[
                const SizedBox(width: 8),
                Container(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 7, vertical: 3),
                  decoration: BoxDecoration(
                    color: kc.hakliSoft,
                    borderRadius: KararRadius.all(8),
                  ),
                  child: Text(
                    '+%${topic.growthPercent}',
                    style: AppTypography.meta.copyWith(
                      fontWeight: FontWeight.w800,
                      letterSpacing: 0,
                      color: kc.hakliText,
                      fontFeatures: AppTypography.tabular,
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _TrendCardSkeleton extends StatelessWidget {
  const _TrendCardSkeleton();

  @override
  Widget build(BuildContext context) {
    return ExcludeSemantics(
      child: KararSurface(
        radius: KararRadius.surface,
        padding: const EdgeInsets.fromLTRB(16, 18, 16, 14),
        child: KararShimmer(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const SkeletonBlock(width: 120, height: 16, radius: 6),
              const SizedBox(height: 16),
              for (int i = 0; i < 4; i++) ...[
                const Row(
                  children: [
                    SkeletonBlock(width: 14, height: 14, radius: 4),
                    SizedBox(width: 16),
                    Expanded(child: SkeletonBlock(height: 14, radius: 6)),
                    SizedBox(width: 24),
                    SkeletonBlock(width: 40, height: 18, radius: 8),
                  ],
                ),
                const SizedBox(height: 18),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
