import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
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

class _StatsContent extends StatelessWidget {
  const _StatsContent({required this.stats});

  final PostStats stats;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: Theme.of(context).colorScheme.surfaceContainerHighest.withValues(alpha: 0.3),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: Theme.of(context).dividerColor.withValues(alpha: 0.3),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.bar_chart_rounded, size: 16, color: AppColors.primary),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  'Gönderi İstatistikleri  (sadece sen görürsün)',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.labelSmall?.copyWith(
                        color: AppColors.primary,
                        fontWeight: FontWeight.w700,
                        letterSpacing: 0.3,
                      ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),
          Row(
            children: [
              Expanded(
                child: _StatItem(
                  icon: '👁️',
                  label: '${_formatNumber(stats.viewCount)} görüntülenme',
                ),
              ),
              Expanded(
                child: _StatItem(
                  icon: '📊',
                  label: '%${stats.voteRate} oy kullandı',
                ),
              ),
              Expanded(
                child: _StatItem(
                  icon: '⏱️',
                  label: '${stats.avgReadingSeconds}sn okuma',
                ),
              ),
            ],
          ),
          if (stats.voteTimeline.isNotEmpty) ...[
            const SizedBox(height: 16),
            Text(
              'Oy Zaman Çizelgesi',
              style: Theme.of(context).textTheme.labelSmall?.copyWith(
                    color: AppColors.textSecondary,
                    fontWeight: FontWeight.w600,
                  ),
            ),
            const SizedBox(height: 8),
            SizedBox(
              height: 56,
              child: CustomPaint(
                size: const Size(double.infinity, 56),
                painter: _TimelinePainter(
                  data: stats.voteTimeline,
                  color: AppColors.primary,
                ),
              ),
            ),
            const SizedBox(height: 4),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  '0s',
                  style: Theme.of(context)
                      .textTheme
                      .labelSmall
                      ?.copyWith(color: AppColors.textTertiary),
                ),
                Text(
                  '${stats.voteTimeline.length}s',
                  style: Theme.of(context)
                      .textTheme
                      .labelSmall
                      ?.copyWith(color: AppColors.textTertiary),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }

  String _formatNumber(int n) {
    if (n >= 1000) {
      return '${(n / 1000).toStringAsFixed(1)}B';
    }
    return '$n';
  }
}

class _StatItem extends StatelessWidget {
  const _StatItem({required this.icon, required this.label});

  final String icon;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        Text(icon, style: const TextStyle(fontSize: 14)),
        const SizedBox(width: 5),
        Expanded(
          child: Text(
            label,
            style: Theme.of(context).textTheme.labelMedium?.copyWith(
                  fontWeight: FontWeight.w600,
                  color: AppColors.textSecondary,
                ),
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
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
      ..color = color.withValues(alpha: 0.8)
      ..style = PaintingStyle.fill;

    for (var i = 0; i < data.length; i++) {
      final ratio = data[i] / maxVal;
      final barHeight = size.height * ratio;
      final x = i * (barWidth + gap);
      final y = size.height - barHeight;

      final rRect = RRect.fromRectAndRadius(
        Rect.fromLTWH(x, y, barWidth, barHeight),
        const Radius.circular(3),
      );
      canvas.drawRRect(rRect, paint);
    }
  }

  @override
  bool shouldRepaint(covariant _TimelinePainter oldDelegate) =>
      oldDelegate.data != data;
}

class _StatsSkeleton extends StatelessWidget {
  const _StatsSkeleton();

  @override
  Widget build(BuildContext context) {
    return const Skeleton(height: 120, width: double.infinity, borderRadius: 12);
  }
}
