import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
import 'discover_provider.dart';

class TrendTopicsPanel extends ConsumerWidget {
  const TrendTopicsPanel({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(trendTopicsProvider);
    return async.when(
      data: (topics) => topics.isEmpty
          ? const SizedBox.shrink()
          : _TrendCard(topics: topics),
      loading: () => const _TrendCardSkeleton(),
      error: (_, __) => const SizedBox.shrink(),
    );
  }
}

class _TrendCard extends StatelessWidget {
  const _TrendCard({required this.topics});

  final List<TrendTopic> topics;

  String _formatCount(int n) {
    if (n >= 1000) return '${(n / 1000).toStringAsFixed(0)}B';
    return n.toString();
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
      child: Card(
        clipBehavior: Clip.antiAlias,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
              color: AppColors.haksiz.withValues(alpha: 0.08),
              child: Row(
                children: [
                  const Text('🔥', style: TextStyle(fontSize: 16)),
                  const SizedBox(width: 8),
                  Text(
                    'Trend Konular',
                    style: Theme.of(context).textTheme.labelLarge?.copyWith(
                          color: AppColors.haksiz,
                          fontWeight: FontWeight.w700,
                        ),
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
              child: Column(
                children: [
                  for (int i = 0; i < topics.take(5).length; i++)
                    _TopicRow(
                      rank: i + 1,
                      topic: topics[i],
                      formatCount: _formatCount,
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _TopicRow extends StatelessWidget {
  const _TopicRow({
    required this.rank,
    required this.topic,
    required this.formatCount,
  });

  final int rank;
  final TrendTopic topic;
  final String Function(int) formatCount;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: () => context.push('/search?q=${Uri.encodeQueryComponent(topic.name)}'),
      borderRadius: BorderRadius.circular(6),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Row(
          children: [
            SizedBox(
              width: 20,
              child: Text(
                '$rank.',
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                      color: AppColors.textTertiary,
                      fontWeight: FontWeight.w700,
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
                    style: Theme.of(context)
                        .textTheme
                        .bodySmall
                        ?.copyWith(fontWeight: FontWeight.w700),
                  ),
                  Text(
                    '${formatCount(topic.postCount)} gönderi',
                    style: Theme.of(context).textTheme.labelSmall?.copyWith(
                          color: AppColors.textSecondary,
                        ),
                  ),
                ],
              ),
            ),
            if (topic.growthPercent != null)
              Text(
                '+${topic.growthPercent}%',
                style: Theme.of(context).textTheme.labelSmall?.copyWith(
                      color: AppColors.hakli,
                      fontWeight: FontWeight.w600,
                    ),
              ),
          ],
        ),
      ),
    );
  }
}

class _TrendCardSkeleton extends StatelessWidget {
  const _TrendCardSkeleton();

  @override
  Widget build(BuildContext context) {
    final surface = Theme.of(context).colorScheme.surfaceContainerHighest;
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                  height: 14, width: 100,
                  decoration: BoxDecoration(color: surface, borderRadius: BorderRadius.circular(4))),
              const SizedBox(height: 12),
              for (int i = 0; i < 4; i++) ...[
                Row(children: [
                  Container(width: 16, height: 12,
                      decoration: BoxDecoration(color: surface, borderRadius: BorderRadius.circular(3))),
                  const SizedBox(width: 8),
                  Expanded(child: Container(height: 12,
                      decoration: BoxDecoration(color: surface, borderRadius: BorderRadius.circular(3)))),
                ]),
                const SizedBox(height: 10),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
