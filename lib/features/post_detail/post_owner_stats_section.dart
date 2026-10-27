import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/count_format.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karar_surface.dart';
import '../../shared/widgets/skeleton.dart';
import 'post_stats_provider.dart';

class PostOwnerStatsSection extends ConsumerWidget {
  const PostOwnerStatsSection({super.key, required this.postId});

  final String postId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final statsAsync = ref.watch(postStatsProvider(postId));

    return statsAsync.when(
      loading: () => const _StatsSkeleton(),
      error: (_, __) => const SizedBox.shrink(),
      data: (stats) => _StatsContent(stats: stats),
    );
  }
}

/// Owner-only stats card (spec §6.1.11): title + "(sadece sen görürsün)",
/// muted stat tiles (value 22/800 + label) and the vote timeline in brand
/// purple.
class _StatsContent extends StatelessWidget {
  const _StatsContent({required this.stats});

  final PostStats stats;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final tiles = [
      _StatTile(
        icon: Icons.visibility_outlined,
        value: formatCompactCount(stats.viewCount),
        label: 'görüntülenme',
      ),
      _StatTile(
        icon: Icons.how_to_vote_outlined,
        value: '%${stats.voteRate}',
        label: 'oy kullandı',
      ),
      _StatTile(
        icon: Icons.schedule_rounded,
        value: '${stats.avgReadingSeconds}sn',
        label: 'okuma',
      ),
    ];

    return KararSurface(
      padding: const EdgeInsets.all(18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.bar_chart_rounded, size: 20, color: kc.primary),
              const SizedBox(width: 8),
              Expanded(
                child: Semantics(
                  header: true,
                  child: Text.rich(
                    TextSpan(
                      children: [
                        TextSpan(
                          text: 'Gönderi İstatistikleri',
                          style: AppTypography.bodyStrong.copyWith(
                            fontSize: 16,
                            fontWeight: FontWeight.w800,
                            color: kc.text1,
                          ),
                        ),
                        const TextSpan(text: '  '),
                        TextSpan(
                          text: '(sadece sen görürsün)',
                          style: AppTypography.meta.copyWith(color: kc.text3),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),
          LayoutBuilder(
            builder: (context, constraints) {
              const spacing = 8.0;
              final columns = constraints.maxWidth >= 330 ? 3 : 2;
              final tileWidth =
                  (constraints.maxWidth - spacing * (columns - 1)) / columns;
              return Wrap(
                spacing: spacing,
                runSpacing: spacing,
                children: [
                  for (final tile in tiles)
                    SizedBox(width: tileWidth, child: tile),
                ],
              );
            },
          ),
          if (stats.voteTimeline.isNotEmpty) ...[
            const SizedBox(height: 18),
            Text(
              'Oy Zaman Çizelgesi',
              style: AppTypography.labelS.copyWith(color: kc.text2),
            ),
            const SizedBox(height: 10),
            ExcludeSemantics(
              child: SizedBox(
                height: 56,
                child: CustomPaint(
                  size: const Size(double.infinity, 56),
                  painter: _TimelinePainter(
                    data: stats.voteTimeline,
                    color: kc.primary,
                  ),
                ),
              ),
            ),
            const SizedBox(height: 6),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  '0s',
                  style: AppTypography.meta.copyWith(color: kc.text3),
                ),
                Text(
                  '${stats.voteTimeline.length}s',
                  style: AppTypography.meta.copyWith(color: kc.text3),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

class _StatTile extends StatelessWidget {
  const _StatTile({
    required this.icon,
    required this.value,
    required this.label,
  });

  final IconData icon;
  final String value;
  final String label;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Semantics(
      container: true,
      label: '$value $label',
      child: ExcludeSemantics(
        child: Container(
          padding: const EdgeInsets.fromLTRB(14, 12, 12, 12),
          decoration: BoxDecoration(
            color: kc.muted,
            borderRadius: KararRadius.all(KararRadius.tile),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(icon, size: 18, color: kc.text2),
              const SizedBox(height: 6),
              FittedBox(
                fit: BoxFit.scaleDown,
                alignment: AlignmentDirectional.centerStart,
                child: Text(
                  value,
                  maxLines: 1,
                  style: AppTypography.cardTitle.copyWith(
                    color: kc.text1,
                    fontFeatures: AppTypography.tabular,
                  ),
                ),
              ),
              const SizedBox(height: 2),
              Text(
                label,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: AppTypography.meta.copyWith(color: kc.text2),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _TimelinePainter extends CustomPainter {
  const _TimelinePainter({required this.data, required this.color});

  final List<int> data;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    if (data.isEmpty) return;

    final maxVal = data.reduce((a, b) => a > b ? a : b);
    if (maxVal == 0) return;

    final barWidth = (size.width / data.length) * 0.65;
    final gap = (size.width / data.length) * 0.35;
    final paint = Paint()
      ..color = color
      ..style = PaintingStyle.fill;

    for (var i = 0; i < data.length; i++) {
      final ratio = data[i] / maxVal;
      final barHeight = size.height * ratio;
      final x = i * (barWidth + gap);
      final y = size.height - barHeight;

      final rRect = RRect.fromRectAndRadius(
        Rect.fromLTWH(x, y, barWidth, barHeight),
        Radius.circular(barWidth.clamp(0, 8) / 2),
      );
      canvas.drawRRect(rRect, paint);
    }
  }

  @override
  bool shouldRepaint(covariant _TimelinePainter oldDelegate) =>
      oldDelegate.data != data || oldDelegate.color != color;
}

class _StatsSkeleton extends StatelessWidget {
  const _StatsSkeleton();

  @override
  Widget build(BuildContext context) {
    return const Skeleton(
      height: 160,
      width: double.infinity,
      borderRadius: KararRadius.surface,
    );
  }
}
