import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_endpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/app_colors.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/loading_indicator.dart';

final _transparencyProvider =
    FutureProvider.autoDispose<ModerationTransparencyData>((ref) async {
  final api = ref.read(apiClientProvider);
  final data = await api.getJson<Map<String, dynamic>>(
    ApiEndpoints.moderationTransparency,
  );
  return ModerationTransparencyData.fromJson(data);
});

class ModerationTransparencyScreen extends ConsumerWidget {
  const ModerationTransparencyScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(_transparencyProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Moderasyon Şeffaflığı')),
      body: async.when(
        loading: () => const LoadingIndicator(),
        error: (err, _) => ErrorView(
          message: 'Veriler yüklenemedi.',
          onRetry: () => ref.invalidate(_transparencyProvider),
        ),
        data: (data) => _TransparencyContent(data: data),
      ),
    );
  }
}

class _TransparencyContent extends StatelessWidget {
  const _TransparencyContent({required this.data});

  final ModerationTransparencyData data;

  @override
  Widget build(BuildContext context) {
    return CenteredContent(
      child: ListView(
        padding: const EdgeInsets.all(20),
        children: [
          _Header(periodDays: data.periodDays),
          const SizedBox(height: 24),
          const _SectionTitle('İçerik'),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: _StatCard(
                  label: 'Oluşturulan\ngönderi',
                  value: _fmt(data.postsCreated),
                  icon: Icons.article_outlined,
                  color: AppColors.primary,
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: _StatCard(
                  label: 'Kaldırılan\ngönderi',
                  value: '${_fmt(data.postsRemoved)} (%${data.postRemovalRatePercent.toStringAsFixed(1)})',
                  icon: Icons.remove_circle_outline,
                  color: AppColors.haksiz,
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: _StatCard(
                  label: 'Oluşturulan\nyorum',
                  value: _fmt(data.commentsCreated),
                  icon: Icons.chat_bubble_outline,
                  color: AppColors.primary,
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: _StatCard(
                  label: 'Kaldırılan\nyorum',
                  value: '${_fmt(data.commentsRemoved)} (%${data.commentRemovalRatePercent.toStringAsFixed(1)})',
                  icon: Icons.remove_circle_outline,
                  color: AppColors.haksiz,
                ),
              ),
            ],
          ),
          const SizedBox(height: 24),
          const _SectionTitle('Raporlar'),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: _StatCard(
                  label: 'Alınan\nrapor',
                  value: _fmt(data.reportsReceived),
                  icon: Icons.flag_outlined,
                  color: Colors.orange,
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: _StatCard(
                  label: 'İşlem yapılan\nrapor',
                  value: '${_fmt(data.reportsActioned)} (%${data.reportActionRatePercent.toStringAsFixed(0)})',
                  icon: Icons.check_circle_outline,
                  color: AppColors.hakli,
                ),
              ),
            ],
          ),
          if (data.removalReasons != null) ...[
            const SizedBox(height: 24),
            _SectionTitle('Kaldırma Nedenleri (son ${data.periodDays} gün)'),
            const SizedBox(height: 12),
            _ReasonBars(reasons: data.removalReasons!),
          ],
          const SizedBox(height: 32),
          Text(
            'Bu rapor her 6 saatte bir güncellenir.',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: AppColors.textTertiary,
                ),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 24),
        ],
      ),
    );
  }

  static String _fmt(int v) {
    if (v >= 1000) return '${(v / 1000).toStringAsFixed(1)}B';
    return v.toString();
  }
}

class _Header extends StatelessWidget {
  const _Header({required this.periodDays});

  final int periodDays;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.primary.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        children: [
          const Icon(Icons.shield_outlined, size: 40),
          const SizedBox(height: 8),
          Text(
            'Topluluk Güveni',
            style: Theme.of(context).textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.w800,
                ),
          ),
          const SizedBox(height: 4),
          Text(
            'Son $periodDays günde platformdaki moderasyon faaliyetleri',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: AppColors.textSecondary,
                ),
            textAlign: TextAlign.center,
          ),
        ],
      ),
    );
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    return Text(
      text,
      style: Theme.of(context).textTheme.titleSmall?.copyWith(
            fontWeight: FontWeight.w700,
            color: AppColors.textSecondary,
          ),
    );
  }
}

class _StatCard extends StatelessWidget {
  const _StatCard({
    required this.label,
    required this.value,
    required this.icon,
    required this.color,
  });

  final String label;
  final String value;
  final IconData icon;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.07),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: color.withValues(alpha: 0.15)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: color, size: 22),
          const SizedBox(height: 8),
          Text(
            value,
            style: Theme.of(context).textTheme.titleMedium?.copyWith(
                  fontWeight: FontWeight.w800,
                  color: color,
                ),
          ),
          const SizedBox(height: 4),
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

class _ReasonBars extends StatelessWidget {
  const _ReasonBars({required this.reasons});

  final ModerationReasonData reasons;

  @override
  Widget build(BuildContext context) {
    final entries = [
      ('Taciz / Zorbalık', reasons.harassment),
      ('Nefret söylemi', reasons.hateSpeech),
      ('Spam', reasons.spam),
      ('Yanlış bilgi', reasons.misinformation),
      ('Diğer', reasons.other),
    ];
    final total = entries.fold<int>(0, (s, e) => s + e.$2);

    return Column(
      children: entries.map((entry) {
        final pct = total > 0 ? entry.$2 / total : 0.0;
        return Padding(
          padding: const EdgeInsets.only(bottom: 10),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(entry.$1,
                      style: Theme.of(context).textTheme.bodySmall),
                  Text('${entry.$2}',
                      style: Theme.of(context)
                          .textTheme
                          .bodySmall
                          ?.copyWith(fontWeight: FontWeight.w700)),
                ],
              ),
              const SizedBox(height: 4),
              ClipRRect(
                borderRadius: BorderRadius.circular(4),
                child: LinearProgressIndicator(
                  value: pct,
                  minHeight: 6,
                  backgroundColor: Theme.of(context)
                      .colorScheme
                      .surfaceContainerHighest,
                  valueColor: const AlwaysStoppedAnimation<Color>(AppColors.haksiz),
                ),
              ),
            ],
          ),
        );
      }).toList(),
    );
  }
}

// ── Data models ──────────────────────────────────────────────────────────────

class ModerationTransparencyData {
  const ModerationTransparencyData({
    required this.periodDays,
    required this.postsCreated,
    required this.postsRemoved,
    required this.postRemovalRatePercent,
    required this.commentsCreated,
    required this.commentsRemoved,
    required this.commentRemovalRatePercent,
    required this.reportsReceived,
    required this.reportsActioned,
    required this.reportActionRatePercent,
    this.removalReasons,
  });

  final int periodDays;
  final int postsCreated;
  final int postsRemoved;
  final double postRemovalRatePercent;
  final int commentsCreated;
  final int commentsRemoved;
  final double commentRemovalRatePercent;
  final int reportsReceived;
  final int reportsActioned;
  final double reportActionRatePercent;
  final ModerationReasonData? removalReasons;

  factory ModerationTransparencyData.fromJson(Map<String, dynamic> json) {
    return ModerationTransparencyData(
      periodDays: (json['periodDays'] as int?) ?? 30,
      postsCreated: (json['postsCreated'] as int?) ?? 0,
      postsRemoved: (json['postsRemoved'] as int?) ?? 0,
      postRemovalRatePercent:
          (json['postRemovalRatePercent'] as num?)?.toDouble() ?? 0,
      commentsCreated: (json['commentsCreated'] as int?) ?? 0,
      commentsRemoved: (json['commentsRemoved'] as int?) ?? 0,
      commentRemovalRatePercent:
          (json['commentRemovalRatePercent'] as num?)?.toDouble() ?? 0,
      reportsReceived: (json['reportsReceived'] as int?) ?? 0,
      reportsActioned: (json['reportsActioned'] as int?) ?? 0,
      reportActionRatePercent:
          (json['reportActionRatePercent'] as num?)?.toDouble() ?? 0,
      removalReasons: json['removalReasons'] != null
          ? ModerationReasonData.fromJson(
              json['removalReasons'] as Map<String, dynamic>)
          : null,
    );
  }
}

class ModerationReasonData {
  const ModerationReasonData({
    required this.harassment,
    required this.hateSpeech,
    required this.spam,
    required this.misinformation,
    required this.other,
  });

  final int harassment;
  final int hateSpeech;
  final int spam;
  final int misinformation;
  final int other;

  factory ModerationReasonData.fromJson(Map<String, dynamic> json) {
    return ModerationReasonData(
      harassment: (json['harassment'] as int?) ?? 0,
      hateSpeech: (json['hateSpeech'] as int?) ?? 0,
      spam: (json['spam'] as int?) ?? 0,
      misinformation: (json['misinformation'] as int?) ?? 0,
      other: (json['other'] as int?) ?? 0,
    );
  }
}
