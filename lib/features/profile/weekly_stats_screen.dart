import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_services.dart';
import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/karar_surface.dart';
import 'widgets/column_app_bar.dart';
import 'widgets/hue_tint.dart';

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
    final kc = context.kc;

    return Scaffold(
      appBar: const ColumnAppBar(
        title: Text('Haftalık Vicdan Karnen'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: statsAsync.when(
          data: (stats) => _StatsBody(stats: stats),
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => Center(
            child: Padding(
              padding: const EdgeInsets.all(32),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  ExcludeSemantics(
                    child: Container(
                      width: 88,
                      height: 88,
                      decoration: BoxDecoration(
                        color: kc.haksizSoft,
                        shape: BoxShape.circle,
                      ),
                      child: Icon(Icons.error_outline_rounded,
                          size: 36, color: kc.haksizFill),
                    ),
                  ),
                  const SizedBox(height: 16),
                  Text(
                    'İstatistikler yüklenemedi.',
                    textAlign: TextAlign.center,
                    style: AppTypography.body.copyWith(color: kc.text2),
                  ),
                  const SizedBox(height: 16),
                  KararButton(
                    label: 'Tekrar Dene',
                    icon: Icons.refresh_rounded,
                    expand: false,
                    onPressed: () => ref.refresh(weeklyStatsProvider),
                  ),
                ],
              ),
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
      padding: EdgeInsets.fromLTRB(16, 8, 16, context.navBottomInset + 24),
      children: [
        _WeekHeader(stats: stats),
        const SizedBox(height: 20),
        _StatsGrid(stats: stats),
        const SizedBox(height: 16),
        if (stats.votesGiven > 0) ...[
          _VoteBreakdown(stats: stats),
          const SizedBox(height: 16),
        ],
        _MotivationCard(message: _motivationalMessage()),
      ],
    );
  }
}

class _WeekHeader extends StatelessWidget {
  const _WeekHeader({required this.stats});
  final WeeklyStats stats;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 4),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  stats.weekLabel,
                  style: AppTypography.titleXL.copyWith(color: kc.text1),
                ),
                const SizedBox(height: 4),
                Text(
                  'Haftanın özeti',
                  style: AppTypography.meta.copyWith(color: kc.text3),
                ),
              ],
            ),
          ),
          if (stats.streak > 0)
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
              decoration: BoxDecoration(
                gradient: KararGradients.streak,
                borderRadius: KararRadius.all(KararRadius.pill),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const ExcludeSemantics(
                    child: Text('🔥', style: TextStyle(fontSize: 16)),
                  ),
                  const SizedBox(width: 6),
                  Text(
                    '${stats.streak} gün seri',
                    style: AppTypography.label.copyWith(
                      color: kc.onCapsuleText,
                    ),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}

class _StatsGrid extends StatelessWidget {
  const _StatsGrid({required this.stats});
  final WeeklyStats stats;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final commentTint = hueTint(kc, CategoryStyle.ocean.start);
    final tiles = [
      _StatCard(
        icon: Icons.auto_awesome_rounded,
        label: 'Kazanılan Karma',
        value: '+${stats.karmaEarned}',
        tint: kc.warnSoft,
        iconColor: kc.goldText,
      ),
      _StatCard(
        icon: Icons.how_to_vote_rounded,
        label: 'Verilen Oy',
        value: '${stats.votesGiven}',
        tint: kc.primaryContainer,
        iconColor: kc.primary,
      ),
      _StatCard(
        icon: Icons.article_rounded,
        label: 'Paylaşım',
        value: '${stats.postsCreated}',
        tint: kc.hakliSoft,
        iconColor: kc.hakliText,
      ),
      _StatCard(
        icon: Icons.chat_bubble_rounded,
        label: 'Yorum',
        value: '${stats.commentsPosted}',
        // Ocean tint: the neutral muted disc read as disabled next to the
        // three tinted ones.
        tint: commentTint.bg,
        iconColor: commentTint.fg,
      ),
    ];
    // Wide (desktop/tablet column): one row of four, so the tiles are not
    // mostly empty. Narrow: two rows of two. Rows size to their content so
    // large text never overflows a fixed aspect ratio.
    return LayoutBuilder(
      builder: (context, constraints) {
        if (constraints.maxWidth >= 560) {
          return IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                for (var i = 0; i < tiles.length; i++) ...[
                  if (i > 0) const SizedBox(width: 10),
                  Expanded(child: tiles[i]),
                ],
              ],
            ),
          );
        }
        return _twoByTwo(tiles);
      },
    );
  }

  Widget _twoByTwo(List<Widget> tiles) {
    return Column(
      children: [
        IntrinsicHeight(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Expanded(child: tiles[0]),
              const SizedBox(width: 10),
              Expanded(child: tiles[1]),
            ],
          ),
        ),
        const SizedBox(height: 10),
        IntrinsicHeight(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Expanded(child: tiles[2]),
              const SizedBox(width: 10),
              Expanded(child: tiles[3]),
            ],
          ),
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
    required this.tint,
    required this.iconColor,
  });

  final IconData icon;
  final String label;
  final String value;
  final Color tint;
  final Color iconColor;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return KararSurface(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 14),
      child: MergeSemantics(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            ExcludeSemantics(
              child: Container(
                width: 36,
                height: 36,
                decoration: BoxDecoration(color: tint, shape: BoxShape.circle),
                child: Icon(icon, size: 20, color: iconColor),
              ),
            ),
            const SizedBox(height: 12),
            Text(
              value,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: AppTypography.display.copyWith(
                fontSize: 28,
                height: 32 / 28,
                color: kc.text1,
                fontFeatures: AppTypography.tabular,
              ),
            ),
            const SizedBox(height: 2),
            Text(
              label,
              style: AppTypography.labelS.copyWith(color: kc.text2),
            ),
          ],
        ),
      ),
    );
  }
}

class _VoteBreakdown extends StatelessWidget {
  const _VoteBreakdown({required this.stats});
  final WeeklyStats stats;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final total = stats.hakliGiven + stats.haksizGiven;
    if (total == 0) return const SizedBox.shrink();

    final hakliRatio = stats.hakliGiven / total;
    final hakliPercent = (hakliRatio * 100).round();
    final haksizPercent = 100 - hakliPercent;

    return KararSurface(
      padding: const EdgeInsets.fromLTRB(18, 16, 18, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Oy dağılımın',
            style: AppTypography.bodyStrong.copyWith(
              fontSize: 16,
              color: kc.text1,
            ),
          ),
          const SizedBox(height: 14),
          ExcludeSemantics(
            child: SizedBox(
              height: 14,
              child: Row(
                children: [
                  if (hakliPercent > 0)
                    Flexible(
                      flex: hakliPercent,
                      child: Container(
                        decoration: BoxDecoration(
                          color: kc.hakliFill,
                          borderRadius: KararRadius.all(7),
                        ),
                      ),
                    ),
                  if (hakliPercent > 0 && haksizPercent > 0)
                    const SizedBox(width: 3),
                  if (haksizPercent > 0)
                    Flexible(
                      flex: haksizPercent,
                      child: Container(
                        decoration: BoxDecoration(
                          color: kc.haksizFill,
                          borderRadius: KararRadius.all(7),
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 10),
          Wrap(
            spacing: 20,
            runSpacing: 6,
            children: [
              _VoteLegend(
                hakli: true,
                label: 'Haklı',
                count: stats.hakliGiven,
                percent: hakliPercent,
              ),
              _VoteLegend(
                hakli: false,
                label: 'Haksız',
                count: stats.haksizGiven,
                percent: haksizPercent,
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
    required this.hakli,
    required this.label,
    required this.count,
    required this.percent,
  });

  final bool hakli;
  final String label;
  final int count;
  final int percent;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(
          hakli ? Icons.check_rounded : Icons.close_rounded,
          size: 16,
          color: kc.voteText(hakli),
        ),
        const SizedBox(width: 4),
        Text(
          '$label: $count (%$percent)',
          style: AppTypography.labelS.copyWith(
            color: kc.voteText(hakli),
            fontFeatures: AppTypography.tabular,
          ),
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
    final kc = context.kc;
    return Container(
      padding: const EdgeInsets.fromLTRB(16, 16, 18, 16),
      decoration: BoxDecoration(
        color: kc.primaryContainer,
        borderRadius: KararRadius.all(KararRadius.surface),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const ExcludeSemantics(
            child: Text('💬', style: TextStyle(fontSize: 20)),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              message,
              style: AppTypography.body.copyWith(
                color: kc.onPrimaryContainer,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
