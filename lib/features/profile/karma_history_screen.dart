import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/count_format.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karar_sheet.dart';
import '../../shared/widgets/karar_surface.dart';
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
    final kc = context.kc;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Karma Geçmişi'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: historyAsync.when(
          data: (items) {
            if (items.isEmpty) {
              return Center(
                child: Padding(
                  padding: EdgeInsets.fromLTRB(
                      32, 32, 32, 32 + context.navBottomInset),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      ExcludeSemantics(
                        child: Container(
                          width: 88,
                          height: 88,
                          decoration: BoxDecoration(
                            color: kc.primaryContainer,
                            shape: BoxShape.circle,
                          ),
                          child: Icon(Icons.stars_rounded,
                              size: 36, color: kc.primary),
                        ),
                      ),
                      const SizedBox(height: 16),
                      Text(
                        'Henüz karma kazanmadın.',
                        textAlign: TextAlign.center,
                        style: AppTypography.bodyStrong
                            .copyWith(color: kc.text1),
                      ),
                      const SizedBox(height: 8),
                      Text(
                        'Paylaşımların ve yorumların oy aldıkça burada görünür.',
                        textAlign: TextAlign.center,
                        style: AppTypography.body.copyWith(color: kc.text2),
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
              padding: EdgeInsets.fromLTRB(
                  16, 8, 16, context.navBottomInset + 24),
              children: [
                _KarmaHero(
                  total: totalKarma,
                  fromPosts: fromPosts,
                  fromComments: fromComments,
                ),
                const SizedBox(height: KararSpace.sectionGap),
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 4),
                  child: Semantics(
                    header: true,
                    child: Text(
                      'Geçmiş',
                      style: AppTypography.headline.copyWith(color: kc.text1),
                    ),
                  ),
                ),
                const SizedBox(height: 12),
                KararSurface(
                  padding: const EdgeInsets.all(4),
                  child: Column(
                    children: [
                      for (var index = 0; index < items.length; index++) ...[
                        if (index > 0)
                          Divider(
                            height: 1,
                            indent: 68,
                            endIndent: 12,
                            color: kc.outlineVariant,
                          ),
                        _KarmaHistoryTile(item: items[index]),
                      ],
                    ],
                  ),
                ),
              ],
            );
          },
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, __) => Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Text(
                'Hata: $e',
                textAlign: TextAlign.center,
                style: AppTypography.body.copyWith(color: kc.text2),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Hero card: total karma (`display`), the level track and the two
/// source tiles (Paylaşım / Yorum).
class _KarmaHero extends StatelessWidget {
  const _KarmaHero({
    required this.total,
    required this.fromPosts,
    required this.fromComments,
  });

  final int total;
  final int fromPosts;
  final int fromComments;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return KararSurface(
      padding: const EdgeInsets.fromLTRB(20, 20, 20, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Toplam Karma',
                      style: AppTypography.labelS.copyWith(color: kc.text2),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      formatCompactCount(total),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: AppTypography.display.copyWith(
                        color: kc.text1,
                        fontFeatures: AppTypography.tabular,
                      ),
                    ),
                  ],
                ),
              ),
              ExcludeSemantics(
                child: Container(
                  width: 52,
                  height: 52,
                  decoration: BoxDecoration(
                    color: kc.warnSoft,
                    shape: BoxShape.circle,
                  ),
                  child: Icon(Icons.stars_rounded, size: 28, color: kc.gold),
                ),
              ),
            ],
          ),
          const SizedBox(height: 20),
          _BadgeProgressionRow(karma: total),
          const SizedBox(height: 16),
          Row(
            children: [
              Expanded(
                child: _SourceTile(
                  label: 'Paylaşım',
                  value: '+$fromPosts',
                  icon: Icons.article_rounded,
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: _SourceTile(
                  label: 'Yorum',
                  value: '+$fromComments',
                  icon: Icons.chat_bubble_rounded,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _SourceTile extends StatelessWidget {
  const _SourceTile({
    required this.label,
    required this.value,
    required this.icon,
  });

  final String label;
  final String value;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Container(
      padding: const EdgeInsets.fromLTRB(12, 12, 12, 12),
      decoration: BoxDecoration(
        color: kc.muted,
        borderRadius: KararRadius.all(KararRadius.input),
      ),
      child: Row(
        children: [
          ExcludeSemantics(
            child: Container(
              width: 32,
              height: 32,
              decoration: BoxDecoration(
                color: kc.primaryContainer,
                borderRadius: KararRadius.all(KararRadius.sm),
              ),
              child: Icon(icon, size: 18, color: kc.primary),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  value,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.headline.copyWith(
                    fontSize: 18,
                    color: kc.text1,
                    fontFeatures: AppTypography.tabular,
                  ),
                ),
                Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.meta.copyWith(color: kc.text2),
                ),
              ],
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
    final kc = context.kc;
    final isPositive = item.karmaDelta > 0;

    return MergeSemantics(
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 64),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(12, 10, 16, 10),
          child: Row(
            children: [
              ExcludeSemantics(
                child: Container(
                  width: 44,
                  height: 44,
                  decoration: BoxDecoration(
                    color: kc.voteSoft(isPositive),
                    shape: BoxShape.circle,
                  ),
                  child: Icon(
                    isPositive
                        ? Icons.trending_up_rounded
                        : Icons.trending_down_rounded,
                    color: kc.voteFill(isPositive),
                    size: 22,
                  ),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      _buildTitle(item),
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: AppTypography.bodyStrong.copyWith(color: kc.text1),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      item.createdAgo,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: AppTypography.meta.copyWith(color: kc.text3),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 8),
              ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 80),
                child: Text(
                  '${isPositive ? '+' : ''}${item.karmaDelta}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  textAlign: TextAlign.end,
                  style: AppTypography.buttonL.copyWith(
                    color: kc.voteText(isPositive),
                    fontFeatures: AppTypography.tabular,
                  ),
                ),
              ),
            ],
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

/// Level track: the five badge levels on a 6 px progress track (gold fill
/// up to the current level), current level labelled. Taps open the
/// existing badge sheet.
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
    final kc = context.kc;
    final currentLabel = karmaBadgeLabel(karma);
    final currentIndex =
        _badges.indexWhere((b) => b.label == currentLabel).clamp(0, 4);

    return Semantics(
      button: true,
      label: '${karmaBadgeLabel(karma)} rozeti — detay için dokun',
      excludeSemantics: true,
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          borderRadius: KararRadius.all(KararRadius.input),
          onTap: () => showKararSheet<void>(
            context,
            builder: (_) => KarmaBadgeSheet(karma: karma),
          ),
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 6),
            child: Column(
              children: [
                Row(
                  children: [
                    for (final b in _badges)
                      Expanded(
                        child: Center(
                          child: Opacity(
                            opacity: karma >= b.threshold ? 1 : 0.35,
                            child: Text(
                              b.emoji,
                              style: const TextStyle(fontSize: 24),
                            ),
                          ),
                        ),
                      ),
                  ],
                ),
                const SizedBox(height: 8),
                LayoutBuilder(
                  builder: (context, constraints) {
                    final w = constraints.maxWidth;
                    final fill = w * (currentIndex + 0.5) / _badges.length;
                    return SizedBox(
                      height: 12,
                      child: Stack(
                        alignment: Alignment.centerLeft,
                        children: [
                          Container(
                            height: 6,
                            decoration: BoxDecoration(
                              color: kc.muted,
                              borderRadius: KararRadius.all(3),
                            ),
                          ),
                          Container(
                            width: fill,
                            height: 6,
                            decoration: BoxDecoration(
                              color: kc.gold,
                              borderRadius: KararRadius.all(3),
                            ),
                          ),
                          Positioned(
                            left: fill - 6,
                            child: Container(
                              width: 12,
                              height: 12,
                              decoration: BoxDecoration(
                                color: kc.gold,
                                shape: BoxShape.circle,
                                border: Border.all(color: kc.surface, width: 2),
                              ),
                            ),
                          ),
                        ],
                      ),
                    );
                  },
                ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    for (var i = 0; i < _badges.length; i++)
                      Expanded(
                        child: Text(
                          _badges[i].label,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          textAlign: TextAlign.center,
                          style: AppTypography.meta.copyWith(
                            fontWeight:
                                i == currentIndex ? FontWeight.w800 : FontWeight.w600,
                            color: i == currentIndex
                                ? kc.goldText
                                : (karma >= _badges[i].threshold
                                    ? kc.text2
                                    : kc.text3),
                          ),
                        ),
                      ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
