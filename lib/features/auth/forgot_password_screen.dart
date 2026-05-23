import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/providers.dart';
import '../../shared/widgets/centered_content.dart';

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

  @override
  void dispose() {
    _emailCtrl.dispose();
    _otpCtrl.dispose();
    _newPasswordCtrl.dispose();
    _confirmCtrl.dispose();
    super.dispose();
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
      if (mounted) setState(() => _error = e.message);
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
      if (mounted) setState(() => _error = e.message);
    } catch (_) {
      if (mounted) setState(() => _error = 'Şifre sıfırlanamadı. Tekrar dene.');
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        leading: InkWell(
          onTap: () => context.go('/'),
          child: Padding(
            padding: const EdgeInsets.all(8.0),
            child: Image.asset('logo/logo.png', fit: BoxFit.contain),
          ),
        ),
        title: const Text('Şifre Sıfırla'),
        centerTitle: true,
      ),
      body: SafeArea(
        child: CenteredContent(
          maxWidth: 400,
          child: SingleChildScrollView(
            padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 32),
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
                  onTogglePassword: () => setState(() => _obscurePassword = !_obscurePassword),
                  onToggleConfirm: () => setState(() => _obscureConfirm = !_obscureConfirm),
                  isLoading: _isLoading,
                  error: _error,
                  onSubmit: _resetPassword,
                  onResend: () { setState(() { _step = _Step.email; _error = null; }); },
                ),
              _Step.done => _DoneStep(onLogin: () => context.go('/auth/login')),
            },
          ),
        ),
      ),
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
    final textTheme = Theme.of(context).textTheme;
    final colorScheme = Theme.of(context).colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Icon(Icons.lock_reset_rounded, size: 64, color: Colors.amber),
        const SizedBox(height: 24),
        Text(
          'Şifreni mi unuttun?',
          style: textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.bold),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 8),
        Text(
          'E-posta adresini gir. Şifre sıfırlama kodu göndereceğiz.',
          style: textTheme.bodyMedium?.copyWith(color: colorScheme.onSurfaceVariant),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 32),
        TextField(
          controller: controller,
          keyboardType: TextInputType.emailAddress,
          textInputAction: TextInputAction.done,
          enabled: !isLoading,
          onSubmitted: (_) => onSubmit(),
          decoration: const InputDecoration(
            labelText: 'E-posta',
            prefixIcon: Icon(Icons.email_outlined),
            border: OutlineInputBorder(),
          ),
        ),
        if (error != null) ...[
          const SizedBox(height: 16),
          Text(
            error!,
            style: TextStyle(color: colorScheme.error, fontSize: 13),
            textAlign: TextAlign.center,
          ),
        ],
        const SizedBox(height: 24),
        FilledButton(
          onPressed: isLoading ? null : onSubmit,
          style: FilledButton.styleFrom(
            minimumSize: const Size.fromHeight(56),
          ),
          child: isLoading
              ? const SizedBox(width: 24, height: 24, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : const Text('Kod Gönder'),
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
    required this.onSubmit,
    required this.onResend,
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
  final VoidCallback onSubmit;
  final VoidCallback onResend;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    final colorScheme = Theme.of(context).colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Icon(Icons.mark_email_read_outlined, size: 64, color: Colors.green),
        const SizedBox(height: 24),
        Text(
          'Kodu Doğrula',
          style: textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.bold),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 8),
        Text.rich(
          TextSpan(
            text: 'Kodu şu adrese gönderdik:\n',
            children: [TextSpan(text: email, style: const TextStyle(fontWeight: FontWeight.bold))],
          ),
          style: textTheme.bodyMedium?.copyWith(color: colorScheme.onSurfaceVariant),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 32),
        TextField(
          controller: otpCtrl,
          keyboardType: TextInputType.number,
          textInputAction: TextInputAction.next,
          enabled: !isLoading,
          decoration: const InputDecoration(
            labelText: '6 Haneli Kod',
            prefixIcon: Icon(Icons.pin_outlined),
            border: OutlineInputBorder(),
            hintText: '123456',
          ),
        ),
        const SizedBox(height: 20),
        TextField(
          controller: newPasswordCtrl,
          obscureText: obscurePassword,
          textInputAction: TextInputAction.next,
          enabled: !isLoading,
          decoration: InputDecoration(
            labelText: 'Yeni Şifre',
            prefixIcon: const Icon(Icons.lock_outline),
            border: const OutlineInputBorder(),
            suffixIcon: IconButton(
              icon: Icon(obscurePassword ? Icons.visibility_off : Icons.visibility),
              onPressed: onTogglePassword,
            ),
          ),
        ),
        const SizedBox(height: 20),
        TextField(
          controller: confirmCtrl,
          obscureText: obscureConfirm,
          textInputAction: TextInputAction.done,
          enabled: !isLoading,
          onSubmitted: (_) => onSubmit(),
          decoration: InputDecoration(
            labelText: 'Şifre Tekrar',
            prefixIcon: const Icon(Icons.lock_reset_outlined),
            border: const OutlineInputBorder(),
            suffixIcon: IconButton(
              icon: Icon(obscureConfirm ? Icons.visibility_off : Icons.visibility),
              onPressed: onToggleConfirm,
            ),
          ),
        ),
        if (error != null) ...[
          const SizedBox(height: 16),
          Text(
            error!,
            style: TextStyle(color: colorScheme.error, fontSize: 13),
            textAlign: TextAlign.center,
          ),
        ],
        const SizedBox(height: 24),
        FilledButton(
          onPressed: isLoading ? null : onSubmit,
          style: FilledButton.styleFrom(
            minimumSize: const Size.fromHeight(56),
          ),
          child: isLoading
              ? const SizedBox(width: 24, height: 24, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : const Text('Şifreyi Güncelle'),
        ),
        const SizedBox(height: 12),
        TextButton(
          onPressed: isLoading ? null : onResend,
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
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SizedBox(height: 40),
        const Icon(Icons.check_circle_rounded, size: 80, color: Colors.green),
        const SizedBox(height: 24),
        Text(
          'Harika!',
          style: Theme.of(context).textTheme.headlineMedium?.copyWith(fontWeight: FontWeight.bold),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 8),
        const Text(
          'Şifren başarıyla güncellendi. Artık yeni şifrenle giriş yapabilirsin.',
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 40),
        FilledButton(
          onPressed: onLogin,
          style: FilledButton.styleFrom(
            minimumSize: const Size.fromHeight(56),
          ),
          child: const Text('Giriş Yapmaya Git'),
        ),
      ],
    );
  }
}

