import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../features/feed/post_repository.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/karar_sheet.dart';
import '../../shared/widgets/rate_limit_ui.dart';

const _reasons = [
  ('hate_speech', 'Nefret Söylemi'),
  ('harassment', 'Taciz / Zorbalık'),
  ('personal_info', 'Kişisel Bilgi Paylaşımı'),
  ('misinformation', 'Yanlış Bilgi'),
  ('spam', 'Spam'),
  ('self_harm', 'Kendine Zarar Verme'),
  ('illegal', 'Yasadışı İçerik'),
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
      showKararSheet<void>(
        context,
        scrollControlled: true,
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
      final messenger = ScaffoldMessenger.of(context);
      final router = GoRouter.of(context);
      Navigator.pop(context);
      messenger.showSnackBar(
        SnackBar(
          content: const Text('Şikayetiniz alındı. Teşekkürler.'),
          action: SnackBarAction(
            label: 'Durumunu Görüntüle',
            onPressed: () => router.push('/settings/moderation-history'),
          ),
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
    final kc = context.kc;
    return Padding(
      padding: EdgeInsets.only(
        bottom: MediaQuery.of(context).viewInsets.bottom,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            KararSheetHeader(
              title: 'Şikayet Et',
              onClose: () => Navigator.maybePop(context),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'Bu içeriği neden şikayet ediyorsun?',
                    style: AppTypography.bodyS.copyWith(color: kc.text2),
                  ),
                  const SizedBox(height: 12),
                  RadioGroup<String>(
                    groupValue: _selectedReason,
                    onChanged: (v) => setState(() => _selectedReason = v),
                    child: Column(
                      children: [
                        for (final r in _reasons)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 4),
                            child: RadioListTile<String>(
                              value: r.$1,
                              title: Text(
                                r.$2,
                                style: AppTypography.bodyStrong.copyWith(
                                  fontWeight: _selectedReason == r.$1
                                      ? FontWeight.w800
                                      : FontWeight.w600,
                                  color: kc.text1,
                                ),
                              ),
                              minTileHeight: 52,
                              contentPadding:
                                  const EdgeInsets.symmetric(horizontal: 8),
                              tileColor: kc.muted.withValues(alpha: 0),
                              selectedTileColor: kc.primaryContainer,
                              selected: _selectedReason == r.$1,
                              // Dark: primaryContainer is only 1.1:1 off
                              // the sheet, so the chosen row also gets a
                              // brand rim.
                              shape: RoundedRectangleBorder(
                                borderRadius:
                                    KararRadius.all(KararRadius.input),
                                side: kc.isDark && _selectedReason == r.$1
                                    ? BorderSide(
                                        color:
                                            kc.primary.withValues(alpha: 0.5),
                                      )
                                    : BorderSide.none,
                              ),
                            ),
                          ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _descCtrl,
                    decoration: const InputDecoration(
                      labelText: 'Açıklama (isteğe bağlı)',
                      hintText: 'Ek bilgi vermek istersen…',
                    ),
                    maxLines: 3,
                    maxLength: 300,
                  ),
                  const SizedBox(height: 4),
                  Center(
                    child: TextButton(
                      onPressed: () => context.push('/legal/community'),
                      child: Text(
                        'Topluluk Kurallarını oku',
                        style: AppTypography.labelS.copyWith(
                          color: kc.primaryText,
                          decoration: TextDecoration.underline,
                          decorationColor: kc.primaryText,
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 8),
                  KararButton(
                    label: 'Gönder',
                    size: KararButtonSize.large,
                    isLoading: _isLoading,
                    onPressed: _selectedReason == null ? null : _submit,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
