import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../core/theme/app_colors.dart';

class KvkkBanner extends StatefulWidget {
  const KvkkBanner({super.key});

  @override
  State<KvkkBanner> createState() => _KvkkBannerState();
}

class _KvkkBannerState extends State<KvkkBanner> {
  bool _isVisible = false;
  static const _kKvkkAcceptedKey = 'kvkk_accepted';

  @override
  void initState() {
    super.initState();
    _checkStatus();
  }

  Future<void> _checkStatus() async {
    final prefs = await SharedPreferences.getInstance();
    final accepted = prefs.getBool(_kKvkkAcceptedKey) ?? false;
    if (!accepted && mounted) {
      setState(() => _isVisible = true);
    }
  }

  Future<void> _accept() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_kKvkkAcceptedKey, true);
    if (mounted) {
      setState(() => _isVisible = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (!_isVisible) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final linkStyle = TextStyle(
      color: theme.colorScheme.primary,
      fontWeight: FontWeight.bold,
      decoration: TextDecoration.underline,
    );

    return Container(
      width: double.infinity,
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHigh,
        border: Border(
          top: BorderSide(color: theme.dividerColor.withValues(alpha: 0.1)),
        ),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.05),
            blurRadius: 10,
            offset: const Offset(0, -2),
          ),
        ],
      ),
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            RichText(
              textAlign: TextAlign.center,
              text: TextSpan(
                style: theme.textTheme.bodySmall?.copyWith(height: 1.5),
                children: [
                  const TextSpan(
                    text: 'Karar\'ı kullanarak ',
                  ),
                  TextSpan(
                    text: 'Kullanım Koşulları',
                    style: linkStyle,
                    recognizer: TapGestureRecognizer()
                      ..onTap = () => context.push('/legal/terms'),
                  ),
                  const TextSpan(text: ' ve '),
                  TextSpan(
                    text: 'Gizlilik Politikası',
                    style: linkStyle,
                    recognizer: TapGestureRecognizer()
                      ..onTap = () => context.push('/legal/privacy'),
                  ),
                  const TextSpan(
                    text: ' hükümlerini okuduğunu ve kabul ettiğini onaylamış olursun.',
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            SizedBox(
              width: double.infinity,
              child: FilledButton(
                onPressed: _accept,
                style: FilledButton.styleFrom(
                  backgroundColor: AppColors.accent,
                  visualDensity: VisualDensity.compact,
                ),
                child: const Text('Anladım'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
