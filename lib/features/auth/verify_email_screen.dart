import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_service.dart';
import '../../core/utils/validators.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/karar_logo.dart';

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
    final colorScheme = Theme.of(context).colorScheme;
    final textTheme = Theme.of(context).textTheme;

    return Scaffold(
      appBar: AppBar(
        leadingWidth: 180,
        leading: InkWell(
          onTap: () => context.go('/'),
          child: const Padding(
            padding: EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: KararLogo(size: LogoSize.medium),
          ),
        ),
        title: const Text('Doğrulama'),
        centerTitle: true,
      ),
      body: SafeArea(
        child: CenteredContent(
          maxWidth: 400,
          child: SingleChildScrollView(
            padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 32),
            child: Form(
              key: _formKey,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(Icons.mark_email_unread_rounded, size: 64, color: Colors.blue),
                  const SizedBox(height: 24),
                  Text(
                    'E-postanı Doğrula',
                    style: textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.bold),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    '${widget.email} adresine 6 haneli bir kod gönderdik.',
                    style: textTheme.bodyMedium?.copyWith(color: colorScheme.onSurfaceVariant),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 40),
                  TextFormField(
                    controller: _otpCtrl,
                    decoration: const InputDecoration(
                      labelText: 'Doğrulama Kodu',
                      prefixIcon: Icon(Icons.pin_outlined),
                      border: OutlineInputBorder(),
                      hintText: '000000',
                      counterText: '',
                    ),
                    keyboardType: TextInputType.number,
                    maxLength: 6,
                    textAlign: TextAlign.center,
                    style: textTheme.headlineMedium?.copyWith(
                      letterSpacing: 12,
                      fontWeight: FontWeight.bold,
                    ),
                    onFieldSubmitted: (_) => _verify(),
                    validator: Validators.otp,
                  ),
                  if (_error != null) ...[
                    const SizedBox(height: 16),
                    Text(
                      _error!,
                      style: TextStyle(color: colorScheme.error, fontSize: 13),
                      textAlign: TextAlign.center,
                    ),
                  ],
                  const SizedBox(height: 32),
                  KararButton(
                    label: 'Hesabı Doğrula',
                    onPressed: _verify,
                    isLoading: _isVerifying,
                  ),
                  const SizedBox(height: 16),
                  KararButton(
                    label: _resendCooldown > 0
                        ? 'Tekrar gönder (${_resendCooldown}s)'
                        : 'Kodu Tekrar Gönder',
                    onPressed: (_isResending || _resendCooldown > 0)
                        ? null
                        : _resend,
                    variant: KararButtonVariant.text,
                    isLoading: _isResending,
                  ),
                  const SizedBox(height: 24),
                  TextButton(
                    onPressed: () => context.go('/auth/login'),
                    child: const Text('Giriş Ekranına Dön'),
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

