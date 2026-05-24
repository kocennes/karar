import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/providers.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/karar_logo.dart';

class ChangeEmailScreen extends ConsumerStatefulWidget {
  const ChangeEmailScreen({super.key});

  @override
  ConsumerState<ChangeEmailScreen> createState() => _ChangeEmailScreenState();
}

class _ChangeEmailScreenState extends ConsumerState<ChangeEmailScreen> {
  final _newEmailCtrl = TextEditingController();
  final _passwordCtrl = TextEditingController();
  final _otpCtrl = TextEditingController();

  late _Step _step;
  var _isLoading = false;
  var _obscurePassword = true;
  String? _error;
  String? _newEmail;

  @override
  void initState() {
    super.initState();
    final pending = ref.read(pendingEmailChangeProvider);
    if (pending != null) {
      _newEmail = pending;
      _step = _Step.confirm;
    } else {
      _step = _Step.request;
    }
  }

  @override
  void dispose() {
    _newEmailCtrl.dispose();
    _passwordCtrl.dispose();
    _otpCtrl.dispose();
    super.dispose();
  }

  Future<void> _requestChange() async {
    final newEmail = _newEmailCtrl.text.trim();
    final password = _passwordCtrl.text;
    if (newEmail.isEmpty) {
      setState(() => _error = 'Yeni e-posta adresini gir.');
      return;
    }
    if (password.isEmpty) {
      setState(() => _error = 'Şifreni gir.');
      return;
    }
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      await ref.read(authServiceProvider).changeEmail(
            newEmail: newEmail,
            password: password,
          );
      await ref.read(authServiceProvider).setPendingEmailChange(newEmail);
      if (mounted) {
        ref.read(pendingEmailChangeProvider.notifier).state = newEmail;
        setState(() { _newEmail = newEmail; _step = _Step.confirm; });
      }
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } catch (_) {
      if (mounted) setState(() => _error = 'İstek gönderilemedi. Tekrar dene.');
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _confirmChange() async {
    final otp = _otpCtrl.text.trim();
    if (otp.isEmpty) {
      setState(() => _error = 'Doğrulama kodunu gir.');
      return;
    }
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      await ref.read(authServiceProvider).confirmEmailChange(
            newEmail: _newEmail!,
            otp: otp,
          );
      await ref.read(authServiceProvider).clearPendingEmailChange();
      if (mounted) {
        ref.read(pendingEmailChangeProvider.notifier).state = null;
        setState(() => _step = _Step.done);
      }
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } catch (_) {
      if (mounted) setState(() => _error = 'E-posta değiştirilemedi. Tekrar dene.');
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, result) async {
        if (didPop) return;

        if (_step == _Step.done ||
            (_newEmailCtrl.text.isEmpty &&
                _passwordCtrl.text.isEmpty &&
                _otpCtrl.text.isEmpty)) {
          if (context.mounted) Navigator.pop(context);
          return;
        }

        final shouldPop = await showDialog<bool>(
          context: context,
          builder: (ctx) => AlertDialog(
            title: const Text('Vazgeçilsin mi?'),
            content: const Text(
                'Değişiklikler kaydedilmeyecek. Çıkmak istiyor musun?'),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(ctx, false),
                child: const Text('Hayır'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(ctx, true),
                child: const Text('Evet'),
              ),
            ],
          ),
        );

        if (shouldPop == true && context.mounted) {
          Navigator.pop(context);
        }
      },
      child: Scaffold(
        appBar: AppBar(
          leadingWidth: 160,
          leading: InkWell(
            onTap: () => context.go('/'),
            child: const Padding(
              padding: EdgeInsets.symmetric(horizontal: 16, vertical: 8),
              child: KararLogo(size: LogoSize.medium),
            ),
          ),
          title: const Text('E-posta Değiştir'),
          centerTitle: true,
        ),
        body: SafeArea(
          child: CenteredContent(
            maxWidth: 400,
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(24),
              child: switch (_step) {
                _Step.request => _RequestStep(
                    newEmailCtrl: _newEmailCtrl,
                    passwordCtrl: _passwordCtrl,
                    obscurePassword: _obscurePassword,
                    onTogglePassword: () =>
                        setState(() => _obscurePassword = !_obscurePassword),
                    isLoading: _isLoading,
                    error: _error,
                    onSubmit: _requestChange,
                  ),
                _Step.confirm => _ConfirmStep(
                    newEmail: _newEmail!,
                    otpCtrl: _otpCtrl,
                    isLoading: _isLoading,
                    error: _error,
                    onSubmit: _confirmChange,
                    onResend: () {
                      setState(() {
                        _step = _Step.request;
                        _error = null;
                      });
                    },
                  ),
                _Step.done => _DoneStep(onDone: () => context.pop()),
              },
            ),
          ),
        ),
      ),
    );
  }
}

enum _Step { request, confirm, done }

class _RequestStep extends StatelessWidget {
  const _RequestStep({
    required this.newEmailCtrl,
    required this.passwordCtrl,
    required this.obscurePassword,
    required this.onTogglePassword,
    required this.isLoading,
    required this.error,
    required this.onSubmit,
  });

  final TextEditingController newEmailCtrl;
  final TextEditingController passwordCtrl;
  final bool obscurePassword;
  final VoidCallback onTogglePassword;
  final bool isLoading;
  final String? error;
  final VoidCallback onSubmit;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SizedBox(height: 8),
        Text(
          'Yeni e-posta adresini ve mevcut şifreni gir. Yeni adrese bir doğrulama kodu göndereceğiz.',
          style: Theme.of(context).textTheme.bodyMedium,
        ),
        const SizedBox(height: 24),
        TextField(
          controller: newEmailCtrl,
          keyboardType: TextInputType.emailAddress,
          textInputAction: TextInputAction.next,
          enabled: !isLoading,
          decoration: const InputDecoration(
            labelText: 'Yeni e-posta',
            prefixIcon: Icon(Icons.email_outlined),
            border: OutlineInputBorder(),
          ),
        ),
        const SizedBox(height: 16),
        TextField(
          controller: passwordCtrl,
          obscureText: obscurePassword,
          textInputAction: TextInputAction.done,
          enabled: !isLoading,
          onSubmitted: (_) => onSubmit(),
          decoration: InputDecoration(
            labelText: 'Mevcut şifre',
            prefixIcon: const Icon(Icons.lock_outline),
            border: const OutlineInputBorder(),
            suffixIcon: IconButton(
              icon: Icon(
                obscurePassword
                    ? Icons.visibility_outlined
                    : Icons.visibility_off_outlined,
              ),
              onPressed: onTogglePassword,
            ),
          ),
        ),
        if (error != null) ...[
          const SizedBox(height: 12),
          Text(
            error!,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
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
              ? const SizedBox(
                  width: 24,
                  height: 24,
                  child: CircularProgressIndicator(
                      strokeWidth: 2, color: Colors.white),
                )
              : const Text('Kod Gönder'),
        ),
      ],
    );
  }
}

class _ConfirmStep extends StatelessWidget {
  const _ConfirmStep({
    required this.newEmail,
    required this.otpCtrl,
    required this.isLoading,
    required this.error,
    required this.onSubmit,
    required this.onResend,
  });

  final String newEmail;
  final TextEditingController otpCtrl;
  final bool isLoading;
  final String? error;
  final VoidCallback onSubmit;
  final VoidCallback onResend;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SizedBox(height: 8),
        Text.rich(
          TextSpan(
            text: 'Kodu şu adrese gönderdik: ',
            children: [
              TextSpan(
                text: newEmail,
                style: const TextStyle(fontWeight: FontWeight.bold),
              ),
            ],
          ),
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.bodyMedium,
        ),
        const SizedBox(height: 24),
        TextField(
          controller: otpCtrl,
          keyboardType: TextInputType.number,
          textInputAction: TextInputAction.done,
          enabled: !isLoading,
          onSubmitted: (_) => onSubmit(),
          textAlign: TextAlign.center,
          decoration: const InputDecoration(
            labelText: 'Doğrulama kodu',
            prefixIcon: Icon(Icons.pin_outlined),
            border: OutlineInputBorder(),
            hintText: '000000',
          ),
        ),
        if (error != null) ...[
          const SizedBox(height: 12),
          Text(
            error!,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
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
              ? const SizedBox(
                  width: 24,
                  height: 24,
                  child: CircularProgressIndicator(
                      strokeWidth: 2, color: Colors.white),
                )
              : const Text('Onayla'),
        ),
        const SizedBox(height: 12),
        TextButton(
          onPressed: isLoading ? null : onResend,
          child: const Text('Kodu tekrar gönder'),
        ),
      ],
    );
  }
}

class _DoneStep extends StatelessWidget {
  const _DoneStep({required this.onDone});
  final VoidCallback onDone;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SizedBox(height: 40),
        const Icon(Icons.check_circle_rounded, size: 80, color: Colors.green),
        const SizedBox(height: 24),
        Text(
          'E-posta güncellendi!',
          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                fontWeight: FontWeight.bold,
              ),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 8),
        const Text(
          'E-posta adresin başarıyla değiştirildi.',
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 40),
        FilledButton(
          onPressed: onDone,
          style: FilledButton.styleFrom(
            minimumSize: const Size.fromHeight(56),
          ),
          child: const Text('Tamam'),
        ),
      ],
    );
  }
}

