import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_service.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/validators.dart';
import '../../shared/widgets/karar_button.dart';
import 'widgets/auth_layout.dart';

class VerifyEmailScreen extends StatefulWidget {
  const VerifyEmailScreen({
    super.key,
    required this.email,
    required this.authService,
    this.onSuccess,
  });

  final String email;
  final AuthService authService;
  final VoidCallback? onSuccess;

  @override
  State<VerifyEmailScreen> createState() => _VerifyEmailScreenState();
}

class _VerifyEmailScreenState extends State<VerifyEmailScreen> {
  final _formKey = GlobalKey<FormState>();
  final _otpCtrl = TextEditingController();
  var _isVerifying = false;
  var _isResending = false;
  var _resendCooldown = 0;
  String? _error;

  @override
  void dispose() {
    _otpCtrl.dispose();
    super.dispose();
  }

  Future<void> _verify() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _isVerifying = true;
      _error = null;
    });
    try {
      await widget.authService.verifyEmail(
        email: widget.email,
        otp: _otpCtrl.text.trim(),
      );
      widget.onSuccess?.call();
    } catch (e) {
      setState(() => _error = e.toString().replaceAll('Exception:', '').trim());
    } finally {
      if (mounted) setState(() => _isVerifying = false);
    }
  }

  Future<void> _resend() async {
    setState(() {
      _isResending = true;
      _error = null;
    });
    try {
      await widget.authService.resendOtp(widget.email);
      _startCooldown();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Kod tekrar gönderildi.')),
        );
      }
    } catch (e) {
      setState(() => _error = e.toString().replaceAll('Exception:', '').trim());
    } finally {
      if (mounted) setState(() => _isResending = false);
    }
  }

  void _startCooldown() {
    setState(() => _resendCooldown = 60);
    Future.doWhile(() async {
      await Future.delayed(const Duration(seconds: 1));
      if (!mounted) return false;
      setState(() => _resendCooldown--);
      return _resendCooldown > 0;
    });
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;

    return AuthScaffold(
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            AuthHeader(
              leading: const AuthDisc(icon: Icons.mark_email_unread_rounded),
              title: 'E-postanı Doğrula',
              subtitle: Text.rich(
                TextSpan(
                  children: [
                    TextSpan(
                      text: widget.email,
                      style: TextStyle(
                        fontWeight: FontWeight.w700,
                        color: kc.text1,
                      ),
                    ),
                    const TextSpan(
                      text: ' adresine 6 haneli bir kod gönderdik.',
                    ),
                  ],
                ),
              ),
            ),
            // Centred OTP field: label, hint and digits share one axis (a
            // start-aligned label + prefix icon sat off the centred code).
            TextFormField(
              controller: _otpCtrl,
              decoration: InputDecoration(
                labelText: 'Doğrulama Kodu',
                floatingLabelAlignment: FloatingLabelAlignment.center,
                // Always floated, so the resting label never sits at the
                // start while the code and hint are centred.
                floatingLabelBehavior: FloatingLabelBehavior.always,
                hintText: '000000',
                hintStyle: AppTypography.headline.copyWith(
                  fontSize: 24,
                  height: 30 / 24,
                  letterSpacing: 10,
                  color: kc.text3,
                  fontFeatures: AppTypography.tabular,
                ),
                counterText: '',
                constraints: const BoxConstraints(minHeight: 56),
              ),
              keyboardType: TextInputType.number,
              maxLength: 6,
              textAlign: TextAlign.center,
              style: AppTypography.headline.copyWith(
                fontSize: 24,
                height: 30 / 24,
                letterSpacing: 10,
                color: kc.text1,
                fontFeatures: AppTypography.tabular,
              ),
              onFieldSubmitted: (_) => _verify(),
              validator: Validators.otp,
            ),
            if (_error != null) ...[
              const SizedBox(height: 12),
              AuthErrorBox(message: _error!),
            ],
            const SizedBox(height: 24),
            KararButton(
              label: 'Hesabı Doğrula',
              size: KararButtonSize.large,
              onPressed: _verify,
              isLoading: _isVerifying,
            ),
            // Both secondary actions: same widget, same size, same gap.
            const SizedBox(height: 8),
            KararButton(
              label: _resendCooldown > 0
                  ? 'Tekrar gönder (${_resendCooldown}s)'
                  : 'Kodu Tekrar Gönder',
              onPressed:
                  (_isResending || _resendCooldown > 0) ? null : _resend,
              variant: KararButtonVariant.text,
              isLoading: _isResending,
            ),
            const SizedBox(height: 8),
            KararButton(
              label: 'Giriş Ekranına Dön',
              onPressed: () => context.go('/auth/login'),
              variant: KararButtonVariant.text,
            ),
          ],
        ),
      ),
    );
  }
}
