import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/auth/auth_service.dart';
import '../../core/utils/validators.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/widgets/karar_button.dart';
import 'widgets/auth_layout.dart';

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
  var _failedAttempts = 0;
  String? _error;

  bool get _isLocked => _failedAttempts >= 5;

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
        totpCode:
            (_showMfaField && !_useBackupCode) ? _totpCtrl.text.trim() : null,
        backupCode: (_showMfaField && _useBackupCode)
            ? _backupCodeCtrl.text.trim()
            : null,
      );
      _failedAttempts = 0;
      widget.onSuccess?.call();
    } on ApiException catch (e) {
      if (e.code == 'EMAIL_NOT_VERIFIED') {
        if (mounted) {
          context.go('/auth/verify-email', extra: _identifierCtrl.text.trim());
        }
        return;
      }
      if (e.code == 'MFA_REQUIRED' || e.code == 'TWO_FACTOR_REQUIRED') {
        setState(() {
          _showMfaField = true;
          _isLoading = false;
        });
        return;
      }
      _failedAttempts++;
      setState(() =>
          _error = _isLocked ? null : _friendlyError(e));
    } catch (e) {
      _failedAttempts++;
      setState(() =>
          _error = _isLocked ? null : _friendlyError(e));
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
    final kc = context.kc;
    final codeStyle = AppTypography.headline.copyWith(
      fontSize: 24,
      height: 30 / 24,
      letterSpacing: 10,
      color: kc.text1,
      fontFeatures: AppTypography.tabular,
    );

    return AuthScaffold(
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const AuthHeader(
              title: 'Tekrar Hoş Geldin',
              subtitle: Text(
                'Hüküm vermek için sabırsızlanan bir topluluk var.',
              ),
            ),
            TextFormField(
              controller: _identifierCtrl,
              decoration: const InputDecoration(
                labelText: 'Kullanıcı adı veya e-posta',
                prefixIcon: Icon(Icons.person_outline_rounded),
              ),
              keyboardType: TextInputType.emailAddress,
              textInputAction: TextInputAction.next,
              validator: (v) =>
                  v == null || v.trim().isEmpty ? 'Bu alan gerekli.' : null,
            ),
            const SizedBox(height: 14),
            TextFormField(
              controller: _passwordCtrl,
              decoration: InputDecoration(
                labelText: 'Şifre',
                prefixIcon: const Icon(Icons.lock_outline_rounded),
                suffixIcon: IconButton(
                  onPressed: () => setState(() => _obscure = !_obscure),
                  icon: Icon(
                    _obscure
                        ? Icons.visibility_off_rounded
                        : Icons.visibility_rounded,
                  ),
                ),
              ),
              obscureText: _obscure,
              textInputAction: TextInputAction.done,
              onFieldSubmitted: (_) => _submit(),
              validator: Validators.password,
            ),
            Align(
              alignment: AlignmentDirectional.centerEnd,
              child: TextButton(
                onPressed: _isLoading
                    ? null
                    : () => context.push('/auth/forgot-password'),
                child: const Text('Şifremi Unuttum'),
              ),
            ),
            if (_showMfaField) ...[
              const SizedBox(height: 6),
              if (!_useBackupCode)
                TextFormField(
                  controller: _totpCtrl,
                  decoration: const InputDecoration(
                    labelText: '2FA Kodu',
                    hintText: '6 haneli doğrulama kodu',
                    prefixIcon: Icon(Icons.shield_rounded),
                  ),
                  style: codeStyle,
                  textAlign: TextAlign.center,
                  keyboardType: TextInputType.number,
                  maxLength: 6,
                  textInputAction: TextInputAction.done,
                  onFieldSubmitted: (_) => _submit(),
                  validator: (v) =>
                      _useBackupCode || (v != null && v.length == 6)
                          ? null
                          : 'Geçerli bir kod girin.',
                )
              else
                TextFormField(
                  controller: _backupCodeCtrl,
                  decoration: const InputDecoration(
                    labelText: 'Yedek Kod',
                    hintText: 'XXXX-XXXX',
                    prefixIcon: Icon(Icons.key_rounded),
                  ),
                  textCapitalization: TextCapitalization.characters,
                  textInputAction: TextInputAction.done,
                  onFieldSubmitted: (_) => _submit(),
                  validator: (v) =>
                      !_useBackupCode || (v != null && v.trim().isNotEmpty)
                          ? null
                          : 'Yedek kodu girin.',
                ),
              Align(
                alignment: AlignmentDirectional.centerEnd,
                child: TextButton(
                  onPressed: () => setState(() {
                    _useBackupCode = !_useBackupCode;
                    _error = null;
                  }),
                  child: Text(
                    _useBackupCode ? '2FA kodunu kullan' : 'Yedek kod kullan',
                  ),
                ),
              ),
            ],
            if (_isLocked) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
                decoration: BoxDecoration(
                  color: kc.warnSoft,
                  borderRadius: KararRadius.all(KararRadius.input),
                ),
                child: Column(
                  children: [
                    Icon(Icons.lock_clock_rounded, color: kc.warnText, size: 32),
                    const SizedBox(height: 8),
                    Text(
                      'Hesabın geçici olarak kilitlendi',
                      style: AppTypography.bodyStrong.copyWith(
                        color: kc.warnText,
                      ),
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      '5 başarısız deneme yapıldı. Lütfen 30 dakika bekle veya şifreni sıfırla.',
                      textAlign: TextAlign.center,
                      style: AppTypography.bodyS.copyWith(color: kc.text1),
                    ),
                    const SizedBox(height: 4),
                    TextButton(
                      onPressed: () => context.push('/auth/forgot-password'),
                      child: const Text('Şifremi Sıfırla'),
                    ),
                  ],
                ),
              ),
            ] else if (_error != null) ...[
              const SizedBox(height: 12),
              AuthErrorBox(message: _error!),
            ],
            const SizedBox(height: 24),
            KararButton(
              label: 'Giriş Yap',
              size: KararButtonSize.large,
              onPressed: _isLocked ? null : _submit,
              isLoading: _isLoading,
            ),
            const SizedBox(height: 16),
            const AuthOrDivider(),
            const SizedBox(height: 16),
            KararButton(
              label: 'Google ile Devam Et',
              onPressed: _isLoading ? null : _googleSignIn,
              variant: KararButtonVariant.outlined,
              size: KararButtonSize.large,
              icon: Icons.g_mobiledata,
            ),
            const SizedBox(height: 20),
            Wrap(
              alignment: WrapAlignment.center,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                Text(
                  'Hesabın yok mu?',
                  style: AppTypography.bodyS.copyWith(color: kc.text2),
                ),
                TextButton(
                  onPressed:
                      _isLoading ? null : () => context.go('/auth/register'),
                  child: const Text('Hemen Kaydol'),
                ),
              ],
            ),
            const SizedBox(height: 8),
            KararButton(
              label: 'Misafir Olarak Dene',
              onPressed: _isLoading ? null : () => Navigator.pop(context),
              variant: KararButtonVariant.tonal,
            ),
          ],
        ),
      ),
    );
  }
}
