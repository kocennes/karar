import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/providers.dart';
import '../../features/feed/post_repository.dart';
import '../../shared/widgets/rate_limit_ui.dart';

const _reasons = [
  ('hate_speech', 'Nefret Söylemi'),
  ('harassment', 'Taciz / Zorbalık'),
  ('doxxing', 'Kişisel Bilgi Paylaşımı (Doxxing)'),
  ('fake_story', 'Sahte / Uydurma İçerik'),
  ('spam', 'Spam'),
  ('self_harm', 'Kendine Zarar Verme'),
  ('illegal_content', 'Yasadışı İçerik'),
  ('other', 'Diğer'),
];

class ReportBottomSheet extends ConsumerStatefulWidget {
  const ReportBottomSheet({
    super.key,
    required this.targetType,
    required this.targetId,
    required this.repository,
  });

  final String targetType;
  final String targetId;
  final PostRepository repository;

  static Future<void> show(
    BuildContext context, {
    required String targetType,
    required String targetId,
    required PostRepository repository,
  }) =>
      showModalBottomSheet<void>(
        context: context,
        showDragHandle: true,
        isScrollControlled: true,
        builder: (_) => ReportBottomSheet(
          targetType: targetType,
          targetId: targetId,
          repository: repository,
        ),
      );

  @override
  ConsumerState<ReportBottomSheet> createState() => _ReportBottomSheetState();
}

class _ReportBottomSheetState extends ConsumerState<ReportBottomSheet> {
  String? _selectedReason;
  final _descCtrl = TextEditingController();
  var _isLoading = false;

  @override
  void dispose() {
    _descCtrl.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (_selectedReason == null) return;
    setState(() => _isLoading = true);
    try {
      await widget.repository.report(
        targetType: widget.targetType,
        targetId: widget.targetId,
        reason: _selectedReason!,
        description:
            _descCtrl.text.trim().isEmpty ? null : _descCtrl.text.trim(),
      );

      ref.read(analyticsServiceProvider).logContentReported(
            reason: _selectedReason!,
            targetType: widget.targetType,
          );

      if (!mounted) return;
      Navigator.pop(context);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Şikayetiniz alındı. Teşekkürler.'),
        ),
      );
    } on ApiException catch (e) {
      if (!mounted) return;
      RateLimitUi.showSnackBar(
        context,
        error: e,
        action: RateLimitedAction.report,
        onRetry: _submit,
      );
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(
        16,
        0,
        16,
        MediaQuery.of(context).viewInsets.bottom + 16,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Şikayet Et',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 4),
          Text(
            'Bu içeriği neden şikayet ediyorsun?',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
          ),
          const SizedBox(height: 16),
          RadioGroup<String>(
            groupValue: _selectedReason,
            onChanged: (v) => setState(() => _selectedReason = v),
            child: Column(
              children: [
                for (final r in _reasons)
                  RadioListTile<String>(
                    value: r.$1,
                    title: Text(r.$2),
                    dense: true,
                    contentPadding: EdgeInsets.zero,
                  ),
              ],
            ),
          ),
          const SizedBox(height: 8),
          TextField(
            controller: _descCtrl,
            decoration: const InputDecoration(
              labelText: 'Açıklama (isteğe bağlı)',
              hintText: 'Ek bilgi vermek istersen…',
              border: OutlineInputBorder(),
            ),
            maxLines: 3,
            maxLength: 300,
          ),
          const SizedBox(height: 8),
          Center(
            child: TextButton(
              onPressed: () => context.push('/legal/community'),
              style: TextButton.styleFrom(
                visualDensity: VisualDensity.compact,
              ),
              child: Text(
                'Topluluk Kurallarını oku',
                style: Theme.of(context).textTheme.labelSmall?.copyWith(
                      color: Theme.of(context).colorScheme.primary,
                      decoration: TextDecoration.underline,
                    ),
              ),
            ),
          ),
          const SizedBox(height: 4),
          FilledButton(
            onPressed: (_selectedReason == null || _isLoading) ? null : _submit,
            child: _isLoading
                ? const SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Text('Gönder'),
          ),
        ],
      ),
    );
  }
}
