import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';

final weeklyStatsProvider =
    FutureProvider.autoDispose<WeeklyStats>((ref) async {
  if (!AppRuntime.useRemoteApi) {
    return const WeeklyStats(
      weekLabel: '12–18 Mayıs',
      karmaEarned: 47,
      votesGiven: 31,
      hakliGiven: 19,
      haksizGiven: 12,
      postsCreated: 2,
      commentsPosted: 8,
      streak: 5,
    );
  }
  return ref.watch(postRepositoryProvider).fetchWeeklyStats();
});

class WeeklyStatsScreen extends ConsumerWidget {
  const WeeklyStatsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final statsAsync = ref.watch(weeklyStatsProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Haftalık Vicdan Karnen'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: statsAsync.when(
          data: (stats) => _StatsBody(stats: stats),
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.error_outline,
                    size: 48, color: AppColors.textTertiary),
                const SizedBox(height: 12),
                const Text('İstatistikler yüklenemedi.'),
                const SizedBox(height: 8),
                TextButton(
                  onPressed: () => ref.refresh(weeklyStatsProvider),
                  child: const Text('Tekrar Dene'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}


class _StatsBody extends StatelessWidget {
  const _StatsBody({required this.stats});
  final WeeklyStats stats;

  String _motivationalMessage() {
    final total = stats.votesGiven + stats.postsCreated + stats.commentsPosted;
    if (total == 0) {
      return 'Bu hafta henüz bir şey yapmadın. Topluluk seni bekliyor!';
    } else if (total < 5) {
      return 'Güzel bir başlangıç. Daha fazla katılım seni daha güçlü yapar.';
    } else if (total < 20) {
      return 'Aktif bir haftaydı. Topluluğa değer katıyorsun!';
    } else {
      return 'Muhteşem! Bu hafta platformun en aktif seslerinden birisin.';
    }
  }

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        _WeekHeader(stats: stats),
        const SizedBox(height: 16),
        _StatsGrid(stats: stats),
        const SizedBox(height: 16),
        if (stats.votesGiven > 0) ...[
          _VoteBreakdown(stats: stats),
          const SizedBox(height: 16),
        ],
        _MotivationCard(message: _motivationalMessage()),
        const SizedBox(height: 8),
      ],
    );
  }
}

class _WeekHeader extends StatelessWidget {
  const _WeekHeader({required this.stats});
  final WeeklyStats stats;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                stats.weekLabel,
                style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                      fontWeight: FontWeight.w900,
                    ),
              ),
              const SizedBox(height: 4),
              Text(
                'Haftanın özeti',
                style: Theme.of(context)
                    .textTheme
                    .bodySmall
                    ?.copyWith(color: AppColors.textSecondary),
              ),
            ],
          ),
        ),
        if (stats.streak > 0)
          Container(
            padding:
                const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            decoration: BoxDecoration(
              gradient: LinearGradient(
                colors: [
                  AppColors.haksiz.withValues(alpha: 0.8),
                  Colors.orange.withValues(alpha: 0.9),
                ],
              ),
              borderRadius: BorderRadius.circular(14),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Text('🔥', style: TextStyle(fontSize: 18)),
                const SizedBox(width: 4),
                Text(
                  '${stats.streak} gün seri',
                  style: const TextStyle(
                    color: Colors.white,
                    fontWeight: FontWeight.w800,
                    fontSize: 13,
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }
}

class _StatsGrid extends StatelessWidget {
  const _StatsGrid({required this.stats});
  final WeeklyStats stats;

  @override
  Widget build(BuildContext context) {
    return GridView.count(
      crossAxisCount: 2,
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      mainAxisSpacing: 10,
      crossAxisSpacing: 10,
      childAspectRatio: 1.6,
      children: [
        _StatCard(
          icon: Icons.auto_awesome_rounded,
          label: 'Kazanılan Karma',
          value: '+${stats.karmaEarned}',
          color: AppColors.accent,
          highlighted: true,
        ),
        _StatCard(
          icon: Icons.how_to_vote_outlined,
          label: 'Verilen Oy',
          value: '${stats.votesGiven}',
          color: AppColors.primary,
        ),
        _StatCard(
          icon: Icons.article_outlined,
          label: 'Paylaşım',
          value: '${stats.postsCreated}',
          color: AppColors.hakli,
        ),
        _StatCard(
          icon: Icons.chat_bubble_outline,
          label: 'Yorum',
          value: '${stats.commentsPosted}',
          color: AppColors.textSecondary,
        ),
      ],
    );
  }
}

class _StatCard extends StatelessWidget {
  const _StatCard({
    required this.icon,
    required this.label,
    required this.value,
    required this.color,
    this.highlighted = false,
  });

  final IconData icon;
  final String label;
  final String value;
  final Color color;
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final bg = highlighted
        ? color.withValues(alpha: 0.12)
        : (isDark ? AppColors.darkSurfaceVariant : AppColors.surfaceVariant);

    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(14),
        border: highlighted
            ? Border.all(color: color.withValues(alpha: 0.3))
            : null,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, size: 18, color: color),
          const SizedBox(height: 8),
          Text(
            value,
            style: TextStyle(
              fontSize: 22,
              fontWeight: FontWeight.w900,
              color: color,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            label,
            style: Theme.of(context).textTheme.labelSmall?.copyWith(
                  color: AppColors.textSecondary,
                ),
          ),
        ],
      ),
    );
  }
}

class _VoteBreakdown extends StatelessWidget {
  const _VoteBreakdown({required this.stats});
  final WeeklyStats stats;

  @override
  Widget build(BuildContext context) {
    final total = stats.hakliGiven + stats.haksizGiven;
    if (total == 0) return const SizedBox.shrink();

    final hakliRatio = stats.hakliGiven / total;

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: Theme.of(context)
            .colorScheme
            .surfaceContainerHighest
            .withValues(alpha: 0.5),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Oy dağılımın',
            style: Theme.of(context).textTheme.labelLarge?.copyWith(
                  fontWeight: FontWeight.w700,
                  color: AppColors.textSecondary,
                ),
          ),
          const SizedBox(height: 12),
          ClipRRect(
            borderRadius: BorderRadius.circular(6),
            child: Row(
              children: [
                Expanded(
                  flex: (hakliRatio * 100).round(),
                  child: Container(
                    height: 12,
                    color: AppColors.hakli,
                  ),
                ),
                Expanded(
                  flex: 100 - (hakliRatio * 100).round(),
                  child: Container(
                    height: 12,
                    color: AppColors.haksiz,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 10),
          Row(
            children: [
              _VoteLegend(
                color: AppColors.hakli,
                label: 'Haklı',
                count: stats.hakliGiven,
                percent: (hakliRatio * 100).round(),
              ),
              const SizedBox(width: 24),
              _VoteLegend(
                color: AppColors.haksiz,
                label: 'Haksız',
                count: stats.haksizGiven,
                percent: 100 - (hakliRatio * 100).round(),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _VoteLegend extends StatelessWidget {
  const _VoteLegend({
    required this.color,
    required this.label,
    required this.count,
    required this.percent,
  });

  final Color color;
  final String label;
  final int count;
  final int percent;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Container(
          width: 10,
          height: 10,
          decoration: BoxDecoration(color: color, shape: BoxShape.circle),
        ),
        const SizedBox(width: 6),
        Text(
          '$label: $count (%$percent)',
          style: Theme.of(context)
              .textTheme
              .bodySmall
              ?.copyWith(color: AppColors.textSecondary),
        ),
      ],
    );
  }
}

class _MotivationCard extends StatelessWidget {
  const _MotivationCard({required this.message});
  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.accent.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppColors.accent.withValues(alpha: 0.15)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('💬', style: TextStyle(fontSize: 20)),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              message,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    height: 1.5,
                    color: Theme.of(context).colorScheme.onSurface,
                  ),
            ),
          ),
        ],
      ),
    );
  }
}
