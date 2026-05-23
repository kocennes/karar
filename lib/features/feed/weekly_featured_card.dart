import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
import 'weekly_featured_provider.dart';

class WeeklyFeaturedCard extends ConsumerWidget {
  const WeeklyFeaturedCard({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(weeklyFeaturedProvider);

    return async.when(
      data: (post) => post == null
          ? const SizedBox.shrink()
          : _FeaturedCard(post: post),
      loading: () => const _FeaturedCardSkeleton(),
      error: (_, __) => const SizedBox.shrink(),
    );
  }
}

class _FeaturedCard extends StatelessWidget {
  const _FeaturedCard({required this.post});

  final Post post;

  String _formatCount(int n) {
    if (n >= 1000000) return '${(n / 1000000).toStringAsFixed(1)}M';
    if (n >= 1000) return '${(n / 1000).toStringAsFixed(1)}B';
    return n.toString();
  }

  @override
  Widget build(BuildContext context) {
    final hakliPct = post.hakliPercent;
    final showPct = post.showPercentage;

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
      child: Card(
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: () => context.push('/posts/${post.id}', extra: post),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Container(
                padding:
                    const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                color: AppColors.primary.withValues(alpha: 0.08),
                child: Row(
                  children: [
                    const Text('🏆', style: TextStyle(fontSize: 16)),
                    const SizedBox(width: 8),
                    Text(
                      'Bu Haftanın Kararı',
                      style: Theme.of(context).textTheme.labelLarge?.copyWith(
                            color: AppColors.primary,
                            fontWeight: FontWeight.w700,
                          ),
                    ),
                  ],
                ),
              ),
              Padding(
                padding: const EdgeInsets.all(14),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(
                      '"${post.title}"',
                      style: Theme.of(context)
                          .textTheme
                          .bodyLarge
                          ?.copyWith(fontWeight: FontWeight.w600),
                      maxLines: 3,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 12),
                    // Vote bar
                    ClipRRect(
                      borderRadius: BorderRadius.circular(4),
                      child: SizedBox(
                        height: 6,
                        child: Row(
                          children: [
                            Expanded(
                              flex: hakliPct,
                              child: const ColoredBox(color: AppColors.hakli),
                            ),
                            Expanded(
                              flex: 100 - hakliPct,
                              child: const ColoredBox(color: AppColors.haksiz),
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 6),
                    Row(
                      children: [
                        if (showPct)
                          Text(
                            '%$hakliPct Haklı',
                            style: Theme.of(context)
                                .textTheme
                                .bodySmall
                                ?.copyWith(color: AppColors.hakli),
                          ),
                        const Spacer(),
                        Icon(
                          Icons.comment_outlined,
                          size: 14,
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
                        ),
                        const SizedBox(width: 4),
                        Text(
                          _formatCount(post.commentCount),
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                        const SizedBox(width: 14),
                        Text(
                          'Görüntüle →',
                          style: Theme.of(context).textTheme.bodySmall?.copyWith(
                                color: AppColors.primary,
                                fontWeight: FontWeight.w600,
                              ),
                        ),
                      ],
                    ),
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

class _FeaturedCardSkeleton extends StatelessWidget {
  const _FeaturedCardSkeleton();

  @override
  Widget build(BuildContext context) {
    final surface = Theme.of(context).colorScheme.surfaceContainerHighest;
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                  height: 14, width: 140, decoration: BoxDecoration(color: surface, borderRadius: BorderRadius.circular(4))),
              const SizedBox(height: 12),
              Container(
                  height: 14, decoration: BoxDecoration(color: surface, borderRadius: BorderRadius.circular(4))),
              const SizedBox(height: 6),
              Container(
                  height: 14, width: 200, decoration: BoxDecoration(color: surface, borderRadius: BorderRadius.circular(4))),
              const SizedBox(height: 12),
              Container(
                  height: 6, decoration: BoxDecoration(color: surface, borderRadius: BorderRadius.circular(4))),
            ],
          ),
        ),
      ),
    );
  }
}
