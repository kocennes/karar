import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/auth/auth_service.dart';
import '../../core/utils/validators.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/karar_logo.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key, required this.authService, this.onSuccess});

  final AuthService authService;
  final VoidCallback? onSuccess;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _identifierCtrl = TextEditingController();
  final _passwordCtrl = TextEditingController();
  final _totpCtrl = TextEditingController();
  final _backupCodeCtrl = TextEditingController();
  var _isLoading = false;
  var _obscure = true;
  var _showMfaField = false;
  var _useBackupCode = false;
  String? _error;

  @override
  void dispose() {
    _identifierCtrl.dispose();
    _passwordCtrl.dispose();
    _totpCtrl.dispose();
    _backupCodeCtrl.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      await widget.authService.loginWithPassword(
        identifier: _identifierCtrl.text.trim(),
        password: _passwordCtrl.text,
        totpCode: (_showMfaField && !_useBackupCode) ? _totpCtrl.text.trim() : null,
        backupCode: (_showMfaField && _useBackupCode) ? _backupCodeCtrl.text.trim() : null,
      );
      widget.onSuccess?.call();
    } on ApiException catch (e) {
      if (e.code == 'EMAIL_NOT_VERIFIED') {
        if (mounted) context.go('/auth/verify-email', extra: _identifierCtrl.text.trim());
        return;
      }
      if (e.code == 'MFA_REQUIRED' || e.code == 'TWO_FACTOR_REQUIRED') {
        setState(() {
          _showMfaField = true;
          _isLoading = false;
        });
        return;
      }
      setState(() => _error = _friendlyError(e));
    } catch (e) {
      setState(() => _error = _friendlyError(e));
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _googleSignIn() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      await widget.authService.loginWithGoogle();
      widget.onSuccess?.call();
    } catch (e) {
      setState(() => _error = _friendlyError(e));
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  String _friendlyError(Object e) => e
      .toString()
      .replaceAll('Exception:', '')
      .replaceAll('ApiException:', '')
      .trim();

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final textTheme = Theme.of(context).textTheme;

    return Scaffold(
      appBar: AppBar(
        leadingWidth: 160,
        leading: InkWell(
          onTap: () => context.go('/'),
          child: const Padding(
            padding: EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: KararLogo(size: LogoSize.medium),
          ),
        ),
        title: const Text('Giriş Yap'),
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
                  Hero(
                    tag: 'auth_logo',
                    child: Icon(
                      Icons.gavel_rounded,
                      size: 64,
                      color: colorScheme.primary,
                    ),
                  ),
                  const SizedBox(height: 24),
                  Text(
                    'Tekrar Hoş Geldin',
                    style: textTheme.headlineMedium?.copyWith(
                      fontWeight: FontWeight.bold,
                    ),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Hüküm vermek için sabırsızlanan bir topluluk var.',
                    style: textTheme.bodyMedium?.copyWith(
                      color: colorScheme.onSurfaceVariant,
                    ),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 40),
                  TextFormField(
                    controller: _identifierCtrl,
                    decoration: const InputDecoration(
                      labelText: 'Kullanıcı adı veya e-posta',
                      prefixIcon: Icon(Icons.person_outline),
                      border: OutlineInputBorder(),
                    ),
                    keyboardType: TextInputType.emailAddress,
                    textInputAction: TextInputAction.next,
                    validator: (v) =>
                        v == null || v.trim().isEmpty ? 'Bu alan gerekli.' : null,
                  ),
                  const SizedBox(height: 20),
                  TextFormField(
                    controller: _passwordCtrl,
                    decoration: InputDecoration(
                      labelText: 'Şifre',
                      prefixIcon: const Icon(Icons.lock_outline),
                      border: const OutlineInputBorder(),
                      suffixIcon: IconButton(
                        onPressed: () => setState(() => _obscure = !_obscure),
                        icon: Icon(
                          _obscure ? Icons.visibility_off : Icons.visibility,
                        ),
                      ),
                    ),
                    obscureText: _obscure,
                    textInputAction: TextInputAction.done,
                    onFieldSubmitted: (_) => _submit(),
                    validator: Validators.password,
                  ),
                  if (_showMfaField) ...[
                    const SizedBox(height: 20),
                    if (!_useBackupCode)
                      TextFormField(
                        controller: _totpCtrl,
                        decoration: const InputDecoration(
                          labelText: '2FA Kodu',
                          hintText: '6 haneli doğrulama kodu',
                          prefixIcon: Icon(Icons.shield_outlined),
                          border: OutlineInputBorder(),
                        ),
                        keyboardType: TextInputType.number,
                        maxLength: 6,
                        textInputAction: TextInputAction.done,
                        onFieldSubmitted: (_) => _submit(),
                        validator: (v) => _useBackupCode || (v != null && v.length == 6)
                            ? null
                            : 'Geçerli bir kod girin.',
                      )
                    else
                      TextFormField(
                        controller: _backupCodeCtrl,
                        decoration: const InputDecoration(
                          labelText: 'Yedek Kod',
                          hintText: 'XXXX-XXXX',
                          prefixIcon: Icon(Icons.key_outlined),
                          border: OutlineInputBorder(),
                        ),
                        textCapitalization: TextCapitalization.characters,
                        textInputAction: TextInputAction.done,
                        onFieldSubmitted: (_) => _submit(),
                        validator: (v) => !_useBackupCode || (v != null && v.trim().isNotEmpty)
                            ? null
                            : 'Yedek kodu girin.',
                      ),
                    Align(
                      alignment: Alignment.centerRight,
                      child: TextButton(
                        onPressed: () => setState(() {
                          _useBackupCode = !_useBackupCode;
                          _error = null;
                        }),
                        child: Text(
                          _useBackupCode ? '2FA kodunu kullan' : 'Yedek kod kullan',
                          style: const TextStyle(fontSize: 13),
                        ),
                      ),
                    ),
                  ],
                  if (_error != null) ...[
                    const SizedBox(height: 16),
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: colorScheme.errorContainer.withOpacity(0.3),
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Text(
                        _error!,
                        style: TextStyle(
                          color: colorScheme.error,
                          fontSize: 13,
                        ),
                        textAlign: TextAlign.center,
                      ),
                    ),
                  ],
                  const SizedBox(height: 32),
                  KararButton(
                    label: 'Giriş Yap',
                    onPressed: _submit,
                    isLoading: _isLoading,
                  ),
                  const SizedBox(height: 16),
                  Row(children: [
                    const Expanded(child: Divider()),
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 16),
                      child: Text(
                        'veya',
                        style: textTheme.bodySmall?.copyWith(
                          color: colorScheme.outline,
                        ),
                      ),
                    ),
                    const Expanded(child: Divider()),
                  ]),
                  const SizedBox(height: 16),
                  KararButton(
                    label: 'Google ile Devam Et',
                    onPressed: _isLoading ? null : _googleSignIn,
                    variant: KararButtonVariant.outlined,
                    icon: Icons.g_mobiledata,
                  ),
                  const SizedBox(height: 16),
                  TextButton(
                    onPressed: _isLoading ? null : () => context.push('/auth/forgot-password'),
                    child: const Text('Şifremi Unuttum'),
                  ),
                  const SizedBox(height: 8),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      const Text('Hesabın yok mu?'),
                      TextButton(
                        onPressed: _isLoading
                            ? null
                            : () => context.go('/auth/register'),
                        child: const Text('Hemen Kaydol'),
                      ),
                    ],
                  ),
                  const SizedBox(height: 24),
                  KararButton(
                    label: 'Misafir Olarak Dene',
                    onPressed: _isLoading ? null : () => Navigator.pop(context),
                    variant: KararButtonVariant.text,
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

