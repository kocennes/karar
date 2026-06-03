import 'package:flutter/material.dart';

enum FeedbackReason {
  toksik('toksik', 'Toksik içerik', Icons.warning_amber_outlined),
  tekrarli('tekrarlı', 'Çok sık görüyorum', Icons.refresh_outlined),
  ilgilenmiyorum('ilgilenmiyorum', 'İlgilenmiyorum', Icons.do_not_disturb_alt_outlined),
  siyasiFazla('siyasi_fazla', 'Çok siyasi', Icons.how_to_vote_outlined),
  kalitesizYorum('kalitesiz_yorum', 'Düşük kalite', Icons.thumb_down_alt_outlined);

  const FeedbackReason(this.value, this.label, this.icon);

  final String value;
  final String label;
  final IconData icon;
}

class FeedbackReasonBottomSheet extends StatelessWidget {
  const FeedbackReasonBottomSheet({super.key});

  static Future<String?> show(BuildContext context) {
    return showModalBottomSheet<String>(
      context: context,
      useRootNavigator: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      builder: (_) => const FeedbackReasonBottomSheet(),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 20, 16, 8),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Neden görmek istemiyorsun?',
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              'Geri bildiriminle akışını iyileştireceğiz.',
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 12),
            ...FeedbackReason.values.map(
              (reason) => ListTile(
                leading: Icon(reason.icon, size: 22),
                title: Text(reason.label),
                contentPadding: EdgeInsets.zero,
                visualDensity: VisualDensity.compact,
                onTap: () => Navigator.of(context, rootNavigator: true).pop(reason.value),
              ),
            ),
            const Divider(height: 16),
            ListTile(
              leading: const Icon(Icons.close, size: 22),
              title: const Text('Vazgeç'),
              contentPadding: EdgeInsets.zero,
              visualDensity: VisualDensity.compact,
              onTap: () => Navigator.of(context, rootNavigator: true).pop(),
            ),
            const SizedBox(height: 4),
          ],
        ),
      ),
    );
  }
}
