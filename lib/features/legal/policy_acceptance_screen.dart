import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_service.dart';
import '../../core/theme/app_colors.dart';

class PolicyAcceptanceScreen extends StatefulWidget {
  const PolicyAcceptanceScreen({
    super.key,
    required this.policyStatus,
    required this.authService,
  });

  final PolicyStatus policyStatus;
  final AuthService authService;

  @override
  State<PolicyAcceptanceScreen> createState() => _PolicyAcceptanceScreenState();
}

class _PolicyAcceptanceScreenState extends State<PolicyAcceptanceScreen> {
  bool _loading = false;
  bool _termsAccepted = false;
  bool _privacyAccepted = false;
  String? _error;

  bool get _canSubmit => _termsAccepted && _privacyAccepted && !_loading;

  Future<void> _accept() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      await widget.authService.acceptPolicy(
        termsVersion: widget.policyStatus.currentTermsVersion,
        privacyVersion: widget.policyStatus.currentPrivacyVersion,
      );
      if (mounted) context.pop();
    } catch (_) {
      if (mounted) {
        setState(() {
          _error = 'Bir hata oluştu. Lütfen tekrar dene.';
          _loading = false;
        });
      }
    }
  }

  Future<void> _reject() async {
    await widget.authService.logout();
    if (mounted) context.go('/auth/login');
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return PopScope(
      canPop: false,
      child: Scaffold(
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const SizedBox(height: 24),
                Icon(Icons.policy_rounded, size: 48, color: AppColors.primary),
                const SizedBox(height: 20),
                Text(
                  'Politikalarımızı Güncelledik',
                  style: theme.textTheme.headlineSmall?.copyWith(
                    fontWeight: FontWeight.bold,
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  'Kullanım Koşullarımız veya Gizlilik Politikamız güncellendi. '
                  'Uygulamayı kullanmaya devam etmek için güncel politikaları kabul etmen gerekiyor.',
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: AppColors.textSecondary,
                    height: 1.5,
                  ),
                ),
                const SizedBox(height: 32),
                _PolicyCheckTile(
                  accepted: _termsAccepted,
                  onChanged: (v) => setState(() => _termsAccepted = v ?? false),
                  label: 'Kullanım Koşulları',
                  routePath: '/legal/terms',
                ),
                const SizedBox(height: 12),
                _PolicyCheckTile(
                  accepted: _privacyAccepted,
                  onChanged: (v) =>
                      setState(() => _privacyAccepted = v ?? false),
                  label: 'Gizlilik Politikası',
                  routePath: '/legal/privacy',
                ),
                if (_error != null) ...[
                  const SizedBox(height: 16),
                  Text(
                    _error!,
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: theme.colorScheme.error,
                    ),
                  ),
                ],
                const Spacer(),
                SizedBox(
                  width: double.infinity,
                  child: FilledButton(
                    onPressed: _canSubmit ? _accept : null,
                    style: FilledButton.styleFrom(
                      backgroundColor: AppColors.primary,
                      padding: const EdgeInsets.symmetric(vertical: 16),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(12),
                      ),
                    ),
                    child: _loading
                        ? const SizedBox(
                            height: 20,
                            width: 20,
                            child: CircularProgressIndicator(
                              strokeWidth: 2,
                              color: Colors.white,
                            ),
                          )
                        : const Text(
                            'Kabul Et ve Devam Et',
                            style: TextStyle(
                              fontSize: 16,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                  ),
                ),
                const SizedBox(height: 8),
                Center(
                  child: TextButton(
                    onPressed: _loading ? null : _reject,
                    child: Text(
                      'Kabul etmiyorum — Çıkış yap',
                      style: TextStyle(
                        color: AppColors.textSecondary,
                        fontSize: 13,
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _PolicyCheckTile extends StatelessWidget {
  const _PolicyCheckTile({
    required this.accepted,
    required this.onChanged,
    required this.label,
    required this.routePath,
  });

  final bool accepted;
  final ValueChanged<bool?> onChanged;
  final String label;
  final String routePath;

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: Theme.of(context).colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: accepted
              ? AppColors.primary.withValues(alpha: 0.4)
              : Colors.transparent,
        ),
      ),
      child: CheckboxListTile(
        value: accepted,
        onChanged: onChanged,
        controlAffinity: ListTileControlAffinity.leading,
        activeColor: AppColors.primary,
        contentPadding:
            const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
        title: RichText(
          text: TextSpan(
            children: [
              TextSpan(
                text: '$label\'nı okudum ve ',
                style: Theme.of(context).textTheme.bodyMedium,
              ),
              WidgetSpan(
                child: GestureDetector(
                  onTap: () => context.push(routePath),
                  child: Text(
                    'kabul ediyorum',
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          color: AppColors.primary,
                          decoration: TextDecoration.underline,
                          decorationColor: AppColors.primary,
                        ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
