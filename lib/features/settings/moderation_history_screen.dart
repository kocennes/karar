import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_exception.dart';
import '../../core/auth/auth_service.dart';
import '../../core/providers.dart';
import '../../core/theme/app_colors.dart';
import '../../shared/widgets/centered_content.dart';

// ── Provider ─────────────────────────────────────────────────────────────────

final moderationHistoryProvider =
    FutureProvider.autoDispose<ModerationSummary>((ref) async {
  return ref.watch(authServiceProvider).fetchModerationHistory();
});

final reportHistoryProvider =
    FutureProvider.autoDispose<ReportHistoryPage>((ref) async {
  return ref.watch(authServiceProvider).fetchReportHistory();
});

// ── Screen ───────────────────────────────────────────────────────────────────

class ModerationHistoryScreen extends ConsumerWidget {
  const ModerationHistoryScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final summaryAsync = ref.watch(moderationHistoryProvider);
    final reportsAsync = ref.watch(reportHistoryProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Moderasyon Geçmişim'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: summaryAsync.when(
          data: (summary) {
            return reportsAsync.when(
              data: (reports) {
                if (summary.events.isEmpty &&
                    summary.removedPosts == 0 &&
                    summary.warnings == 0 &&
                    reports.reports.isEmpty) {
                  return _EmptyState(summary: summary);
                }
                return _HistoryList(summary: summary, reports: reports);
              },
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (_, __) => _HistoryList(
                summary: summary,
                reports: const ReportHistoryPage(reports: []),
                reportsError: true,
              ),
            );
          },
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, __) => Center(
            child: Padding(
              padding: const EdgeInsets.all(32),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(Icons.error_outline,
                      size: 48, color: Theme.of(context).colorScheme.error),
                  const SizedBox(height: 16),
                  const Text('Moderasyon geçmişi yüklenemedi.',
                      textAlign: TextAlign.center),
                  const SizedBox(height: 16),
                  FilledButton.icon(
                    onPressed: () => ref.invalidate(moderationHistoryProvider),
                    icon: const Icon(Icons.refresh),
                    label: const Text('Tekrar Dene'),
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

// ── Sub-widgets ───────────────────────────────────────────────────────────────

class _EmptyState extends StatelessWidget {
  const _EmptyState({required this.summary});
  final ModerationSummary summary;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        _SummaryRow(summary: summary),
        const SizedBox(height: 40),
        const Column(
          children: [
            Icon(Icons.verified_outlined,
                size: 56, color: AppColors.textTertiary),
            SizedBox(height: 16),
            Text(
              'Herhangi bir moderasyon kaydın yok.',
              style: TextStyle(color: AppColors.textSecondary),
              textAlign: TextAlign.center,
            ),
            SizedBox(height: 8),
            Text(
              'Topluluk kurallarına uygun içerikler için teşekkürler.',
              textAlign: TextAlign.center,
              style: TextStyle(color: AppColors.textTertiary, fontSize: 13),
            ),
          ],
        ),
      ],
    );
  }
}

class _HistoryList extends StatelessWidget {
  const _HistoryList({
    required this.summary,
    required this.reports,
    this.reportsError = false,
  });
  final ModerationSummary summary;
  final ReportHistoryPage reports;
  final bool reportsError;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        _SummaryRow(summary: summary),
        if (summary.events.isNotEmpty) ...[
          const SizedBox(height: 20),
          Text(
            'Moderasyon Olayları',
            style: Theme.of(context).textTheme.titleSmall?.copyWith(
                  color: AppColors.textSecondary,
                  fontWeight: FontWeight.w700,
                ),
          ),
          const SizedBox(height: 8),
          ...List.generate(summary.events.length, (i) {
            return Column(
              children: [
                _EventTile(event: summary.events[i]),
                if (i < summary.events.length - 1) const Divider(height: 1),
              ],
            );
          }),
        ],
        const SizedBox(height: 20),
        Text(
          'RaporlarÄ±m',
          style: Theme.of(context).textTheme.titleSmall?.copyWith(
                color: AppColors.textSecondary,
                fontWeight: FontWeight.w700,
              ),
        ),
        const SizedBox(height: 8),
        if (reportsError)
          const Text(
            'Rapor durumlarÄ± yÃ¼klenemedi.',
            style: TextStyle(color: AppColors.textSecondary, fontSize: 13),
          )
        else if (reports.reports.isEmpty)
          const Text(
            'HenÃ¼z gÃ¶nderdiÄŸin bir rapor yok.',
            style: TextStyle(color: AppColors.textSecondary, fontSize: 13),
          )
        else
          ...List.generate(reports.reports.length, (i) {
            return Column(
              children: [
                _ReportTile(report: reports.reports[i]),
                if (i < reports.reports.length - 1) const Divider(height: 1),
              ],
            );
          }),
      ],
    );
  }
}

class _SummaryRow extends StatelessWidget {
  const _SummaryRow({required this.summary});
  final ModerationSummary summary;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: _StatCard(
            icon: Icons.check_circle_outline,
            color: AppColors.hakli,
            value: '${summary.activePosts}',
            label: 'Aktif Gönderi',
          ),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: _StatCard(
            icon: Icons.remove_circle_outline,
            color: AppColors.haksiz,
            value: '${summary.removedPosts}',
            label: 'Kaldırılan',
          ),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: _StatCard(
            icon: Icons.warning_amber_rounded,
            color: Colors.orange,
            value: '${summary.warnings}',
            label: 'Uyarı',
          ),
        ),
      ],
    );
  }
}

class _StatCard extends StatelessWidget {
  const _StatCard({
    required this.icon,
    required this.color,
    required this.value,
    required this.label,
  });

  final IconData icon;
  final Color color;
  final String value;
  final String label;

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14),
      decoration: BoxDecoration(
        color: isDark ? AppColors.darkSurfaceVariant : AppColors.surfaceVariant,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 18, color: color),
          const SizedBox(height: 8),
          Text(
            value,
            style: Theme.of(context)
                .textTheme
                .titleLarge
                ?.copyWith(fontWeight: FontWeight.w900, color: color),
          ),
          const SizedBox(height: 2),
          Text(
            label,
            style: Theme.of(context)
                .textTheme
                .labelSmall
                ?.copyWith(color: AppColors.textSecondary),
          ),
        ],
      ),
    );
  }
}

class _ReportTile extends StatelessWidget {
  const _ReportTile({required this.report});
  final ReportHistoryItem report;

  @override
  Widget build(BuildContext context) {
    final (color, icon) = _statusStyle(report.status);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          CircleAvatar(
            radius: 20,
            backgroundColor: color.withValues(alpha: 0.12),
            child: Icon(Icons.flag_outlined, color: color, size: 18),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Text(
                      _targetLabel(report.targetType),
                      style: const TextStyle(fontWeight: FontWeight.w700),
                    ),
                    const Spacer(),
                    Text(
                      _formatDate(report.createdAt),
                      style: const TextStyle(
                          color: AppColors.textTertiary, fontSize: 12),
                    ),
                  ],
                ),
                const SizedBox(height: 6),
                Container(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                  decoration: BoxDecoration(
                    color: color.withValues(alpha: 0.12),
                    borderRadius: BorderRadius.circular(6),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(icon, size: 12, color: color),
                      const SizedBox(width: 4),
                      Text(
                        report.publicStatus,
                        style: TextStyle(
                          fontSize: 11,
                          color: color,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  'Sebep: ${_reasonLabel(report.reason)}',
                  style: const TextStyle(
                      color: AppColors.textSecondary, fontSize: 13),
                ),
                if (report.publicReason != null) ...[
                  const SizedBox(height: 4),
                  Text(
                    report.publicReason!,
                    style: const TextStyle(
                        color: AppColors.textSecondary, fontSize: 13),
                  ),
                ],
                if (report.targetPreview != null) ...[
                  const SizedBox(height: 4),
                  Text(
                    '"${report.targetPreview}"',
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                        color: AppColors.textTertiary, fontSize: 13),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

  (Color, IconData) _statusStyle(String status) => switch (status) {
        'actioned' => (AppColors.hakli, Icons.check_circle_outline),
        'dismissed' => (AppColors.haksiz, Icons.cancel_outlined),
        'under_review' => (Colors.blue, Icons.manage_search_outlined),
        _ => (Colors.orange, Icons.hourglass_empty_outlined),
      };

  String _targetLabel(String targetType) =>
      targetType == 'comment' ? 'Yorum Raporu' : 'Gönderi Raporu';

  String _reasonLabel(String reason) => switch (reason) {
        'hate_speech' => 'Nefret söylemi',
        'harassment' => 'Taciz / zorbalık',
        'personal_info' => 'Kişisel bilgi',
        'misinformation' => 'Yanlış bilgi',
        'spam' => 'Spam',
        'self_harm' => 'Kendine zarar',
        'illegal' => 'Yasadışı içerik',
        _ => 'Diğer',
      };

  String _formatDate(DateTime dt) {
    const months = [
      'Oca',
      'Şub',
      'Mar',
      'Nis',
      'May',
      'Haz',
      'Tem',
      'Ağu',
      'Eyl',
      'Eki',
      'Kas',
      'Ara',
    ];
    return '${dt.day} ${months[dt.month - 1]}';
  }
}

class _EventTile extends ConsumerStatefulWidget {
  const _EventTile({required this.event});
  final ModerationEvent event;

  @override
  ConsumerState<_EventTile> createState() => _EventTileState();
}

class _EventTileState extends ConsumerState<_EventTile> {
  bool _isSubmitting = false;

  @override
  Widget build(BuildContext context) {
    final event = widget.event;
    final (icon, color) = switch (event.action) {
      'removed' => (Icons.remove_circle_outline, AppColors.haksiz),
      'review' => (Icons.manage_search_outlined, Colors.blueGrey),
      'warning' => (Icons.warning_amber_rounded, Colors.orange),
      'strike' => (Icons.report_outlined, Colors.deepOrange),
      'ban' => (Icons.block_outlined, Colors.red.shade900),
      _ => (Icons.info_outline, AppColors.textSecondary),
    };

    final actionLabel = _decisionLabel(event.action);

    final appealLabel = switch (event.appealStatus) {
      'pending' => 'İtiraz İnceleniyor',
      'approved' => 'İtiraz Onaylandı',
      'rejected' => 'İtiraz Reddedildi',
      _ => null,
    };

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          CircleAvatar(
            radius: 20,
            backgroundColor: color.withValues(alpha: 0.12),
            child: Icon(icon, color: color, size: 18),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Text(
                      _targetLabel(event.targetType),
                      style: const TextStyle(fontWeight: FontWeight.w700),
                    ),
                    const Spacer(),
                    Text(
                      _formatDate(event.createdAt),
                      style: const TextStyle(
                          color: AppColors.textTertiary, fontSize: 12),
                    ),
                  ],
                ),
                if (event.contentExcerpt != null) ...[
                  const SizedBox(height: 4),
                  Text(
                    '"${event.contentExcerpt}"',
                    style: const TextStyle(
                        color: AppColors.textSecondary, fontSize: 13),
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                ],
                const SizedBox(height: 4),
                Text(
                  'Karar: $actionLabel',
                  style: TextStyle(
                    color: color,
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  'Neden: ${event.reason}',
                  style: const TextStyle(
                      color: AppColors.textSecondary, fontSize: 13),
                ),
                if (appealLabel != null) ...[
                  const SizedBox(height: 4),
                  Container(
                    padding:
                        const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                    decoration: BoxDecoration(
                      color: _appealColor(event.appealStatus)
                          .withValues(alpha: 0.12),
                      borderRadius: BorderRadius.circular(6),
                    ),
                    child: Text(
                      appealLabel,
                      style: TextStyle(
                        fontSize: 11,
                        color: _appealColor(event.appealStatus),
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                ],
                if (_canAppeal(event)) ...[
                  const SizedBox(height: 8),
                  OutlinedButton.icon(
                    onPressed:
                        _isSubmitting ? null : () => _showAppealDialog(event),
                    icon: _isSubmitting
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.gavel_outlined, size: 16),
                    label: const Text('İtiraz Et'),
                    style: OutlinedButton.styleFrom(
                      visualDensity: VisualDensity.compact,
                      padding: const EdgeInsets.symmetric(
                          horizontal: 12, vertical: 6),
                    ),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

  Color _appealColor(String status) => switch (status) {
        'approved' => AppColors.hakli,
        'rejected' => AppColors.haksiz,
        _ => Colors.orange,
      };

  String _decisionLabel(String action) => switch (action) {
        'review' => 'Incelemede',
        'removed' => 'İçerik Kaldırıldı',
        'warning' => 'Uyarı',
        'strike' => 'Strike',
        'ban' => 'Hesap Askıya Alındı',
        _ => 'Moderasyon',
      };

  String _targetLabel(String targetType) => switch (targetType) {
        'comment' => 'Yorum',
        'user' => 'Hesap',
        _ => 'Gönderi',
      };

  String _formatDate(DateTime dt) {
    const months = [
      'Oca',
      'Şub',
      'Mar',
      'Nis',
      'May',
      'Haz',
      'Tem',
      'Ağu',
      'Eyl',
      'Eki',
      'Kas',
      'Ara',
    ];
    return '${dt.day} ${months[dt.month - 1]}';
  }

  bool _canAppeal(ModerationEvent event) {
    return event.appealStatus == 'none' &&
        event.action == 'removed' &&
        (event.targetType == 'post' || event.targetType == 'comment');
  }

  Future<void> _showAppealDialog(ModerationEvent event) async {
    final controller = TextEditingController();
    final formKey = GlobalKey<FormState>();
    final reason = await showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('İtiraz Et'),
        content: Form(
          key: formKey,
          child: TextFormField(
            controller: controller,
            minLines: 4,
            maxLines: 6,
            maxLength: 1000,
            autofocus: true,
            decoration: const InputDecoration(
              labelText: 'İtiraz gerekçen',
              hintText: 'Bu kararın neden tekrar incelenmesi gerektiğini yaz.',
              alignLabelWithHint: true,
            ),
            validator: (value) {
              final text = value?.trim() ?? '';
              if (text.length < 20) {
                return 'En az 20 karakter yazmalısın.';
              }
              return null;
            },
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            onPressed: () {
              if (formKey.currentState?.validate() ?? false) {
                Navigator.pop(ctx, controller.text.trim());
              }
            },
            child: const Text('Gönder'),
          ),
        ],
      ),
    );
    controller.dispose();
    if (reason == null || !mounted) return;

    final messenger = ScaffoldMessenger.of(context);
    setState(() => _isSubmitting = true);
    try {
      await ref.read(authServiceProvider).submitModerationAppeal(
            targetType: event.targetType,
            targetId: event.targetId,
            message: reason,
          );
      ref.invalidate(moderationHistoryProvider);
      messenger.showSnackBar(
        const SnackBar(
          content: Text('İtirazın alındı. Sonuç burada görünecek.'),
        ),
      );
    } on ApiException catch (e) {
      messenger.showSnackBar(SnackBar(content: Text(e.friendlyMessage)));
    } catch (_) {
      _openAppeal(event);
      messenger.showSnackBar(
        const SnackBar(
          content: Text(
            'İtiraz kaydedilemedi. destek@karar.app için e-posta taslağı açıldı.',
          ),
        ),
      );
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  void _openAppeal(ModerationEvent event) {
    final subject = Uri.encodeComponent('İçerik İtirazı — ${event.id}');
    final body = Uri.encodeComponent(
      'Kaldırılan içerik için itiraz etmek istiyorum.\n\n'
      'Olay ID: ${event.id}\nNeden: ${event.reason}\n\nİtiraz gerekçem:\n',
    );
    final uri =
        Uri.parse('mailto:destek@karar.app?subject=$subject&body=$body');
    launchUrl(uri).catchError((_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
                'destek@karar.app adresine itiraz e-postası gönderebilirsin.'),
          ),
        );
      }
      return false;
    });
  }
}
