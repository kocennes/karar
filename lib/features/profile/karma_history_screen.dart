import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karma_badge.dart';
import '../../shared/widgets/centered_content.dart';

final karmaHistoryProvider =
    FutureProvider.autoDispose<List<KarmaHistory>>((ref) async {
  return ref.watch(postRepositoryProvider).fetchKarmaHistory();
});

class KarmaHistoryScreen extends ConsumerWidget {
  const KarmaHistoryScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final historyAsync = ref.watch(karmaHistoryProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Karma Geçmişi'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: historyAsync.when(
          data: (items) {
            if (items.isEmpty) {
              return const Center(
                child: Padding(
                  padding: EdgeInsets.all(32),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(Icons.stars_outlined,
                          size: 56, color: AppColors.textTertiary),
                      SizedBox(height: 16),
                      Text(
                        'Henüz karma kazanmadın.',
                        style: TextStyle(color: AppColors.textSecondary),
                      ),
                      SizedBox(height: 8),
                      Text(
                        'Paylaşımların ve yorumların oy aldıkça burada görünür.',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                            color: AppColors.textTertiary, fontSize: 13),
                      ),
                    ],
                  ),
                ),
              );
            }

            final totalKarma = items.fold(0, (sum, i) => sum + i.karmaDelta);
            final fromPosts = items
                .where((i) => i.sourceType == 'post_vote')
                .fold(0, (sum, i) => sum + i.karmaDelta);
            final fromComments = items
                .where((i) => i.sourceType == 'comment_upvote')
                .fold(0, (sum, i) => sum + i.karmaDelta);

            return ListView(
              padding: const EdgeInsets.all(16),
              children: [
                _KarmaStatsRow(
                  total: totalKarma,
                  fromPosts: fromPosts,
                  fromComments: fromComments,
                ),
                const SizedBox(height: 16),
                _BadgeProgressionRow(karma: totalKarma),
                const SizedBox(height: 20),
                Text(
                  'Geçmiş',
                  style: Theme.of(context).textTheme.titleSmall?.copyWith(
                        color: AppColors.textSecondary,
                        fontWeight: FontWeight.w700,
                      ),
                ),
                const SizedBox(height: 8),
                ...List.generate(items.length, (index) {
                  final item = items[index];
                  return Column(
                    children: [
                      _KarmaHistoryTile(item: item),
                      if (index < items.length - 1) const Divider(height: 1),
                    ],
                  );
                }),
              ],
            );
          },
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, __) => Center(child: Text('Hata: $e')),
        ),
      ),
    );
  }
}


class _KarmaStatsRow extends StatelessWidget {
  const _KarmaStatsRow({
    required this.total,
    required this.fromPosts,
    required this.fromComments,
  });

  final int total;
  final int fromPosts;
  final int fromComments;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          flex: 2,
          child: _StatCard(
            label: 'Toplam Karma',
            value: '$total',
            icon: Icons.stars_rounded,
            color: AppColors.accent,
            highlighted: true,
          ),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: _StatCard(
            label: 'Paylaşım',
            value: '+$fromPosts',
            icon: Icons.article_outlined,
            color: AppColors.hakli,
          ),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: _StatCard(
            label: 'Yorum',
            value: '+$fromComments',
            icon: Icons.comment_outlined,
            color: AppColors.primary,
          ),
        ),
      ],
    );
  }
}

class _StatCard extends StatelessWidget {
  const _StatCard({
    required this.label,
    required this.value,
    required this.icon,
    required this.color,
    this.highlighted = false,
  });

  final String label;
  final String value;
  final IconData icon;
  final Color color;
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final bg = highlighted
        ? color.withValues(alpha: 0.12)
        : (isDark ? AppColors.darkSurfaceVariant : AppColors.surfaceVariant);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(12),
        border: highlighted
            ? Border.all(color: color.withValues(alpha: 0.3))
            : null,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 18, color: color),
          const SizedBox(height: 8),
          Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: Theme.of(context).textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.w900,
                  color: color,
                ),
          ),
          const SizedBox(height: 2),
          Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: Theme.of(context).textTheme.labelSmall?.copyWith(
                  color: AppColors.textSecondary,
                ),
          ),
        ],
      ),
    );
  }
}

class _KarmaHistoryTile extends StatelessWidget {
  const _KarmaHistoryTile({required this.item});
  final KarmaHistory item;

  @override
  Widget build(BuildContext context) {
    final isPositive = item.karmaDelta > 0;
    final color = isPositive ? AppColors.hakli : AppColors.haksiz;

    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
      leading: CircleAvatar(
        radius: 20,
        backgroundColor: color.withValues(alpha: 0.12),
        child: Icon(
          isPositive ? Icons.trending_up : Icons.trending_down,
          color: color,
          size: 18,
        ),
      ),
      title: Text(
        _buildTitle(item),
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
      ),
      subtitle: Text(
        item.createdAgo,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: const TextStyle(color: AppColors.textTertiary, fontSize: 12),
      ),
      trailing: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 72),
        child: Text(
          '${isPositive ? '+' : ''}${item.karmaDelta}',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          textAlign: TextAlign.end,
          style: TextStyle(
            fontWeight: FontWeight.w800,
            fontSize: 16,
            color: color,
          ),
        ),
      ),
    );
  }

  String _buildTitle(KarmaHistory item) {
    if (item.sourceType == 'post_vote') {
      return 'Paylaşımın ${item.milestone} oy aldı';
    } else if (item.sourceType == 'comment_upvote') {
      return 'Yorumun ${item.milestone} beğeni aldı';
    }
    return 'Milestone ulaşıldı';
  }
}

class _BadgeProgressionRow extends StatelessWidget {
  const _BadgeProgressionRow({required this.karma});

  final int karma;

  static const _badges = [
    (emoji: '🌱', label: 'Yeni', threshold: 0),
    (emoji: '⚡', label: 'Aktif', threshold: 11),
    (emoji: '🔥', label: 'Popüler', threshold: 51),
    (emoji: '⚖️', label: 'Hakem', threshold: 201),
    (emoji: '👑', label: 'Usta', threshold: 501),
  ];

  @override
  Widget build(BuildContext context) {
    return InkWell(
      borderRadius: BorderRadius.circular(12),
      onTap: () => showModalBottomSheet<void>(
        context: context,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
        ),
        builder: (_) => KarmaBadgeSheet(karma: karma),
      ),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        decoration: BoxDecoration(
          color: Theme.of(context)
              .colorScheme
              .surfaceContainerHighest
              .withValues(alpha: 0.5),
          borderRadius: BorderRadius.circular(12),
        ),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.spaceAround,
          children: _badges.map((b) {
            final isEarned = karma >= b.threshold;
            final isCurrent = karmaBadgeLabel(karma) == b.label;
            return Expanded(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    b.emoji,
                    style: TextStyle(
                      fontSize: 24,
                      color:
                          isEarned ? null : Colors.grey.withValues(alpha: 0.3),
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    b.label,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.labelSmall?.copyWith(
                          fontWeight:
                              isCurrent ? FontWeight.w800 : FontWeight.normal,
                          color: isCurrent
                              ? AppColors.accent
                              : (isEarned
                                  ? AppColors.textSecondary
                                  : AppColors.textTertiary
                                      .withValues(alpha: 0.5)),
                        ),
                  ),
                  if (isCurrent)
                    Container(
                      margin: const EdgeInsets.only(top: 4),
                      height: 3,
                      width: 24,
                      decoration: BoxDecoration(
                        color: AppColors.accent,
                        borderRadius: BorderRadius.circular(2),
                      ),
                    ),
                ],
              ),
            );
          }).toList(),
        ),
      ),
    );
  }
}
