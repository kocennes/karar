import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/widgets/karar_button.dart';
import 'widgets/auth_layout.dart';

class ForgotPasswordScreen extends ConsumerStatefulWidget {
  const ForgotPasswordScreen({super.key});

  @override
  ConsumerState<ForgotPasswordScreen> createState() => _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends ConsumerState<ForgotPasswordScreen> {
  final _emailCtrl = TextEditingController();
  final _otpCtrl = TextEditingController();
  final _newPasswordCtrl = TextEditingController();
  final _confirmCtrl = TextEditingController();

  var _step = _Step.email;
  var _isLoading = false;
  var _obscurePassword = true;
  var _obscureConfirm = true;
  String? _error;
  String? _email;
  int _resendCooldown = 0;

  Timer? _cooldownTimer;

  @override
  void dispose() {
    _cooldownTimer?.cancel();
    _emailCtrl.dispose();
    _otpCtrl.dispose();
    _newPasswordCtrl.dispose();
    _confirmCtrl.dispose();
    super.dispose();
  }

  void _startCooldown(int seconds) {
    _cooldownTimer?.cancel();
    setState(() => _resendCooldown = seconds);
    _cooldownTimer = Timer.periodic(const Duration(seconds: 1), (t) {
      if (!mounted) { t.cancel(); return; }
      setState(() {
        _resendCooldown--;
        if (_resendCooldown <= 0) t.cancel();
      });
    });
  }

  Future<void> _sendCode() async {
    final email = _emailCtrl.text.trim();
    if (email.isEmpty) {
      setState(() => _error = 'E-posta adresini gir.');
      return;
    }
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      await ref.read(authServiceProvider).forgotPassword(email);
      if (mounted) setState(() { _email = email; _step = _Step.verify; });
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.friendlyMessage);
    } catch (_) {
      if (mounted) setState(() => _error = 'Kod gönderilemedi. Tekrar dene.');
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _resendCode() async {
    setState(() { _isLoading = true; _error = null; });
    try {
      await ref.read(authServiceProvider).forgotPassword(_email!);
      if (mounted) setState(() => _error = null);
    } on ApiException catch (e) {
      if (mounted) {
        final wait = e.retryAfterSeconds;
        if (wait != null && wait > 0) _startCooldown(wait);
        setState(() => _error = e.friendlyMessage);
      }
    } catch (_) {
      if (mounted) setState(() => _error = 'Kod gönderilemedi. Tekrar dene.');
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _resetPassword() async {
    final otp = _otpCtrl.text.trim();
    final newPw = _newPasswordCtrl.text;
    final confirm = _confirmCtrl.text;

    if (otp.isEmpty) { setState(() => _error = 'Doğrulama kodunu gir.'); return; }
    if (newPw.length < 8) { setState(() => _error = 'Şifre en az 8 karakter olmalı.'); return; }
    if (newPw != confirm) { setState(() => _error = 'Şifreler eşleşmiyor.'); return; }

    setState(() { _isLoading = true; _error = null; });
    try {
      await ref.read(authServiceProvider).resetPassword(
            email: _email!,
            otp: otp,
            newPassword: newPw,
          );
      if (mounted) setState(() => _step = _Step.done);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.friendlyMessage);
    } catch (_) {
      if (mounted) setState(() => _error = 'Şifre sıfırlanamadı. Tekrar dene.');
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return AuthScaffold(
      child: switch (_step) {
        _Step.email => _EmailStep(
            controller: _emailCtrl,
            isLoading: _isLoading,
            error: _error,
            onSubmit: _sendCode,
          ),
        _Step.verify => _VerifyStep(
            email: _email!,
            otpCtrl: _otpCtrl,
            newPasswordCtrl: _newPasswordCtrl,
            confirmCtrl: _confirmCtrl,
            obscurePassword: _obscurePassword,
            obscureConfirm: _obscureConfirm,
            onTogglePassword: () =>
                setState(() => _obscurePassword = !_obscurePassword),
            onToggleConfirm: () =>
                setState(() => _obscureConfirm = !_obscureConfirm),
            isLoading: _isLoading,
            error: _error,
            resendCooldown: _resendCooldown,
            onSubmit: _resetPassword,
            onResend: _resendCode,
            onChangeEmail: () {
              setState(() {
                _step = _Step.email;
                _error = null;
              });
            },
          ),
        _Step.done => _DoneStep(onLogin: () => context.go('/auth/login')),
      },
    );
  }
}

enum _Step { email, verify, done }

class _EmailStep extends StatelessWidget {
  const _EmailStep({
    required this.controller,
    required this.isLoading,
    required this.error,
    required this.onSubmit,
  });

  final TextEditingController controller;
  final bool isLoading;
  final String? error;
  final VoidCallback onSubmit;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        AuthHeader(
          leading: AuthDisc(
            icon: Icons.lock_reset_rounded,
            background: kc.warnSoft,
            foreground: kc.warnText,
          ),
          title: 'Şifreni mi unuttun?',
          subtitle: const Text(
            'E-posta adresini gir. Şifre sıfırlama kodu göndereceğiz.',
          ),
        ),
        TextField(
          controller: controller,
          keyboardType: TextInputType.emailAddress,
          textInputAction: TextInputAction.done,
          enabled: !isLoading,
          onSubmitted: (_) => onSubmit(),
          decoration: const InputDecoration(
            labelText: 'E-posta',
            prefixIcon: Icon(Icons.email_outlined),
          ),
        ),
        if (error != null) ...[
          const SizedBox(height: 12),
          AuthErrorBox(message: error!),
        ],
        const SizedBox(height: 24),
        KararButton(
          label: 'Kod Gönder',
          size: KararButtonSize.large,
          isLoading: isLoading,
          onPressed: isLoading ? null : onSubmit,
        ),
      ],
    );
  }
}

class _VerifyStep extends StatelessWidget {
  const _VerifyStep({
    required this.email,
    required this.otpCtrl,
    required this.newPasswordCtrl,
    required this.confirmCtrl,
    required this.obscurePassword,
    required this.obscureConfirm,
    required this.onTogglePassword,
    required this.onToggleConfirm,
    required this.isLoading,
    required this.error,
    required this.resendCooldown,
    required this.onSubmit,
    required this.onResend,
    required this.onChangeEmail,
  });

  final String email;
  final TextEditingController otpCtrl;
  final TextEditingController newPasswordCtrl;
  final TextEditingController confirmCtrl;
  final bool obscurePassword;
  final bool obscureConfirm;
  final VoidCallback onTogglePassword;
  final VoidCallback onToggleConfirm;
  final bool isLoading;
  final String? error;
  final int resendCooldown;
  final VoidCallback onSubmit;
  final VoidCallback onResend;
  final VoidCallback onChangeEmail;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        AuthHeader(
          leading: const AuthDisc(icon: Icons.mark_email_read_rounded),
          title: 'Kodu Doğrula',
          subtitle: Text.rich(
            TextSpan(
              text: 'Kodu şu adrese gönderdik:\n',
              children: [
                TextSpan(
                  text: email,
                  style: const TextStyle(fontWeight: FontWeight.w700),
                ),
              ],
            ),
          ),
        ),
        TextField(
          controller: otpCtrl,
          keyboardType: TextInputType.number,
          textInputAction: TextInputAction.next,
          enabled: !isLoading,
          textAlign: TextAlign.center,
          style: AppTypography.headline.copyWith(
            fontSize: 24,
            height: 30 / 24,
            letterSpacing: 10,
            color: kc.text1,
            fontFeatures: AppTypography.tabular,
          ),
          decoration: const InputDecoration(
            labelText: '6 Haneli Kod',
            prefixIcon: Icon(Icons.pin_rounded),
            hintText: '123456',
          ),
        ),
        const SizedBox(height: 14),
        TextField(
          controller: newPasswordCtrl,
          obscureText: obscurePassword,
          textInputAction: TextInputAction.next,
          enabled: !isLoading,
          decoration: InputDecoration(
            labelText: 'Yeni Şifre',
            prefixIcon: const Icon(Icons.lock_outline_rounded),
            suffixIcon: IconButton(
              icon: Icon(
                obscurePassword
                    ? Icons.visibility_off_rounded
                    : Icons.visibility_rounded,
              ),
              onPressed: onTogglePassword,
            ),
          ),
        ),
        const SizedBox(height: 14),
        TextField(
          controller: confirmCtrl,
          obscureText: obscureConfirm,
          textInputAction: TextInputAction.done,
          enabled: !isLoading,
          onSubmitted: (_) => onSubmit(),
          decoration: InputDecoration(
            labelText: 'Şifre Tekrar',
            prefixIcon: const Icon(Icons.lock_reset_rounded),
            suffixIcon: IconButton(
              icon: Icon(
                obscureConfirm
                    ? Icons.visibility_off_rounded
                    : Icons.visibility_rounded,
              ),
              onPressed: onToggleConfirm,
            ),
          ),
        ),
        if (error != null) ...[
          const SizedBox(height: 12),
          AuthErrorBox(message: error!),
        ],
        const SizedBox(height: 24),
        KararButton(
          label: 'Şifreyi Güncelle',
          size: KararButtonSize.large,
          isLoading: isLoading,
          onPressed: isLoading ? null : onSubmit,
        ),
        const SizedBox(height: 12),
        TextButton(
          onPressed: (isLoading || resendCooldown > 0) ? null : onResend,
          child: resendCooldown > 0
              ? Text('Kodu tekrar gönder ($resendCooldown sn)')
              : const Text('Kodu tekrar gönder'),
        ),
        TextButton(
          onPressed: isLoading ? null : onChangeEmail,
          child: const Text('E-postayı yanlış mı girdin?'),
        ),
      ],
    );
  }
}

class _DoneStep extends StatelessWidget {
  const _DoneStep({required this.onLogin});
  final VoidCallback onLogin;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SizedBox(height: 24),
        AuthHeader(
          leading: AuthDisc(
            icon: Icons.check_circle_rounded,
            background: kc.hakliSoft,
            foreground: kc.voteText(true),
          ),
          title: 'Harika!',
          subtitle: const Text(
            'Şifren başarıyla güncellendi. Artık yeni şifrenle giriş yapabilirsin.',
          ),
        ),
        KararButton(
          label: 'Giriş Yapmaya Git',
          size: KararButtonSize.large,
          onPressed: onLogin,
        ),
      ],
    );
  }
}
