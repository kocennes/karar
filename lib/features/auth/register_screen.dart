import 'dart:async';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_service.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/validators.dart';
import '../../shared/widgets/karar_button.dart';
import 'widgets/auth_layout.dart';

enum UsernameStatus { idle, checking, available, taken }

class RegisterScreen extends StatefulWidget {
  const RegisterScreen({
    super.key,
    required this.authService,
    this.returnTo,
    this.onSuccess,
  });

  final AuthService authService;
  final String? returnTo;
  final VoidCallback? onSuccess;

  @override
  State<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends State<RegisterScreen> {
  final _formKey = GlobalKey<FormState>();
  final _usernameCtrl = TextEditingController();
  final _emailCtrl = TextEditingController();
  final _passwordCtrl = TextEditingController();
  final _confirmPasswordCtrl = TextEditingController();
  final _dobCtrl = TextEditingController();

  DateTime? _selectedDob;
  String? _selectedGender;
  bool _ageConfirmed = false;
  bool _acceptedPolicy = false;

  var _isLoading = false;
  var _obscure = true;
  var _obscureConfirm = true;
  String? _error;

  // Username availability check
  Timer? _usernameDebounce;
  UsernameStatus _usernameStatus = UsernameStatus.idle;

  final _confirmPasswordFocus = FocusNode();

  @override
  void initState() {
    super.initState();
    _usernameCtrl.addListener(_onUsernameChanged);
  }

  @override
  void dispose() {
    _usernameCtrl.removeListener(_onUsernameChanged);
    _usernameDebounce?.cancel();
    _usernameCtrl.dispose();
    _emailCtrl.dispose();
    _passwordCtrl.dispose();
    _confirmPasswordCtrl.dispose();
    _dobCtrl.dispose();
    _confirmPasswordFocus.dispose();
    super.dispose();
  }

  void _onUsernameChanged() {
    _usernameDebounce?.cancel();

    final username = _usernameCtrl.text.trim();
    if (username.length < 3) {
      setState(() => _usernameStatus = UsernameStatus.idle);
      return;
    }

    _usernameDebounce = Timer(const Duration(milliseconds: 500), _checkUsername);
  }

  Future<void> _checkUsername() async {
    final username = _usernameCtrl.text.trim();
    if (username.length < 3) return;

    setState(() => _usernameStatus = UsernameStatus.checking);

    try {
      final isAvailable =
          await widget.authService.isUsernameAvailable(username);
      if (!mounted) return;

      // Kullanıcı hala aynı şeyi mi yazıyor kontrol et (race condition)
      if (_usernameCtrl.text.trim() == username) {
        setState(() {
          _usernameStatus =
              isAvailable ? UsernameStatus.available : UsernameStatus.taken;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() => _usernameStatus = UsernameStatus.idle);
      }
    }
  }

  Widget? _buildUsernameSuffix() {
    switch (_usernameStatus) {
      case UsernameStatus.checking:
        return const SizedBox(
          width: 16,
          height: 16,
          child: Padding(
            padding: EdgeInsets.all(12),
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
        );
      case UsernameStatus.available:
        return Icon(Icons.check_circle_rounded, color: context.kc.hakliText);
      case UsernameStatus.taken:
        return Icon(Icons.cancel_rounded, color: context.kc.haksizText);
      case UsernameStatus.idle:
        return null;
    }
  }

  Future<void> _selectDob() async {
    final now = DateTime.now();
    final initialDate = _selectedDob ?? DateTime(2000);
    final picked = await showDatePicker(
      context: context,
      initialDate: initialDate,
      firstDate: DateTime(now.year - 100),
      lastDate: DateTime(now.year - 18, now.month, now.day),
      helpText: 'Doğum Tarihini Seç',
    );
    if (picked != null && picked != _selectedDob) {
      setState(() {
        _selectedDob = picked;
        _dobCtrl.text = "${picked.day}/${picked.month}/${picked.year}";
      });
    }
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;

    if (_selectedDob == null) {
      setState(() => _error = 'Doğum tarihi seçmelisin.');
      return;
    }

    if (_selectedGender == null) {
      setState(() => _error = 'Cinsiyet seçmelisin.');
      return;
    }

    if (_usernameStatus == UsernameStatus.taken) {
      setState(() => _error = 'Bu kullanıcı adı zaten alınmış.');
      return;
    }

    if (_usernameStatus == UsernameStatus.checking) {
      return;
    }

    if (!_ageConfirmed) {
      setState(() => _error = '18 yaşında veya daha büyük olduğunu onaylamalısın.');
      return;
    }

    if (!_acceptedPolicy) {
      setState(
        () => _error =
            'Kullanım koşulları ve topluluk kurallarını kabul etmelisin.',
      );
      return;
    }

    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      final email = await widget.authService.register(
        username: _usernameCtrl.text.trim(),
        email: _emailCtrl.text.trim(),
        password: _passwordCtrl.text,
        dateOfBirth: _selectedDob!,
        gender: _selectedGender!,
        acceptedTerms: _acceptedPolicy,
        acceptedCommunityGuidelines: _acceptedPolicy,
        ageConfirmed: _ageConfirmed,
      );
      if (!mounted) return;
      context.go(_authLocation('/auth/verify-email'), extra: email);
    } catch (e) {
      setState(() => _error = e.toString().replaceAll('Exception:', '').trim());
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
      setState(() => _error = e.toString().replaceAll('Exception:', '').trim());
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
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
            const AuthHeader(
              title: 'Topluluğa Katıl',
              subtitle: Text('Kaydol, fikrini söyle ve adaleti sağla.'),
            ),
            Container(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              decoration: BoxDecoration(
                color: kc.primaryContainer,
                borderRadius: KararRadius.all(KararRadius.input),
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(Icons.info_outline_rounded,
                      size: 20, color: kc.onPrimaryContainer),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      'Lütfen bilgileri doğru girin. Doğum tarihi ve cinsiyetiniz ileride hesap kurtarma işlemlerinde gerekebilir.',
                      style: AppTypography.bodyS.copyWith(
                        color: kc.onPrimaryContainer,
                      ),
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 24),
            TextFormField(
              controller: _usernameCtrl,
              decoration: InputDecoration(
                labelText: 'Kullanıcı adı',
                prefixIcon: const Icon(Icons.alternate_email_rounded),
                helperText: _usernameStatus == UsernameStatus.available
                    ? 'Kullanılabilir'
                    : _usernameStatus == UsernameStatus.taken
                        ? 'Bu kullanıcı adı alınmış'
                        : '3-20 karakter, harf/rakam/alt çizgi',
                helperStyle: AppTypography.meta.copyWith(
                  color: _usernameStatus == UsernameStatus.available
                      ? kc.hakliText
                      : _usernameStatus == UsernameStatus.taken
                          ? kc.haksizText
                          : kc.text3,
                ),
                suffixIcon: _buildUsernameSuffix(),
              ),
              keyboardType: TextInputType.name,
              textInputAction: TextInputAction.next,
              validator: (v) {
                final basic = Validators.username(v);
                if (basic != null) return basic;
                if (_usernameStatus == UsernameStatus.taken) {
                  return 'Bu kullanıcı adı zaten alınmış.';
                }
                return null;
              },
            ),
            const SizedBox(height: 14),
            TextFormField(
              controller: _emailCtrl,
              decoration: const InputDecoration(
                labelText: 'E-posta',
                prefixIcon: Icon(Icons.email_outlined),
              ),
              keyboardType: TextInputType.emailAddress,
              textInputAction: TextInputAction.next,
              validator: Validators.email,
            ),
            const SizedBox(height: 14),
            TextFormField(
              controller: _dobCtrl,
              readOnly: true,
              onTap: _selectDob,
              decoration: const InputDecoration(
                labelText: 'Doğum Tarihi',
                prefixIcon: Icon(Icons.calendar_today_rounded),
                hintText: 'Gün/Ay/Yıl',
              ),
              validator: (v) =>
                  v == null || v.isEmpty ? 'Doğum tarihi gerekli.' : null,
            ),
            const SizedBox(height: 14),
            DropdownButtonFormField<String>(
              initialValue: _selectedGender,
              borderRadius: KararRadius.all(KararRadius.input),
              decoration: const InputDecoration(
                labelText: 'Cinsiyet',
                prefixIcon: Icon(Icons.people_outline_rounded),
              ),
              items: const [
                DropdownMenuItem(value: 'female', child: Text('Kadın')),
                DropdownMenuItem(value: 'male', child: Text('Erkek')),
                DropdownMenuItem(value: 'other', child: Text('Diğer')),
                DropdownMenuItem(
                    value: 'prefer_not_to_say',
                    child: Text('Belirtmek İstemiyorum')),
              ],
              onChanged: (v) => setState(() => _selectedGender = v),
              validator: (v) => v == null ? 'Cinsiyet seçimi gerekli.' : null,
            ),
            const SizedBox(height: 14),
            TextFormField(
              controller: _passwordCtrl,
              onFieldSubmitted: (_) => _confirmPasswordFocus.requestFocus(),
              decoration: InputDecoration(
                labelText: 'Şifre',
                prefixIcon: const Icon(Icons.lock_outline_rounded),
                helperText: 'En az 8 karakter',
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
              textInputAction: TextInputAction.next,
              validator: Validators.password,
            ),
            const SizedBox(height: 14),
            TextFormField(
              controller: _confirmPasswordCtrl,
              focusNode: _confirmPasswordFocus,
              decoration: InputDecoration(
                labelText: 'Şifre Tekrar',
                prefixIcon: const Icon(Icons.lock_reset_rounded),
                suffixIcon: IconButton(
                  onPressed: () =>
                      setState(() => _obscureConfirm = !_obscureConfirm),
                  icon: Icon(
                    _obscureConfirm
                        ? Icons.visibility_off_rounded
                        : Icons.visibility_rounded,
                  ),
                ),
              ),
              obscureText: _obscureConfirm,
              textInputAction: TextInputAction.done,
              onFieldSubmitted: (_) => _submit(),
              validator: (v) => Validators.passwordsMatch(v, _passwordCtrl.text),
            ),
            const SizedBox(height: 16),
            CheckboxListTile(
              value: _ageConfirmed,
              onChanged: _isLoading
                  ? null
                  : (value) => setState(
                        () => _ageConfirmed = value ?? false,
                      ),
              controlAffinity: ListTileControlAffinity.leading,
              contentPadding: const EdgeInsets.symmetric(horizontal: 4),
              shape: RoundedRectangleBorder(
                borderRadius: KararRadius.all(KararRadius.input),
              ),
              title: Text(
                '18 yaşında veya daha büyük olduğumu beyan ediyorum.',
                style: AppTypography.bodyS.copyWith(color: kc.text1),
              ),
            ),
            CheckboxListTile(
              value: _acceptedPolicy,
              onChanged: _isLoading
                  ? null
                  : (value) => setState(
                        () => _acceptedPolicy = value ?? false,
                      ),
              controlAffinity: ListTileControlAffinity.leading,
              contentPadding: const EdgeInsets.symmetric(horizontal: 4),
              shape: RoundedRectangleBorder(
                borderRadius: KararRadius.all(KararRadius.input),
              ),
              title: Text(
                'Kullanım Koşulları ve Topluluk Kurallarını kabul ediyorum.',
                style: AppTypography.bodyS.copyWith(color: kc.text1),
              ),
            ),
            Wrap(
              alignment: WrapAlignment.center,
              spacing: 8,
              children: [
                TextButton(
                  onPressed: () => context.push('/legal/terms'),
                  child: const Text('Kullanım Koşulları'),
                ),
                TextButton(
                  onPressed: () => context.push('/legal/community'),
                  child: const Text('Topluluk Kuralları'),
                ),
              ],
            ),
            if (_error != null) ...[
              const SizedBox(height: 12),
              AuthErrorBox(message: _error!),
            ],
            const SizedBox(height: 24),
            KararButton(
              label: 'Kaydol',
              size: KararButtonSize.large,
              onPressed: _usernameStatus == UsernameStatus.taken || _isLoading
                  ? null
                  : _submit,
              isLoading: _isLoading,
            ),
            const SizedBox(height: 16),
            const AuthOrDivider(),
            const SizedBox(height: 16),
            KararButton(
              label: 'Google ile Kaydol',
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
                  'Zaten hesabın var mı?',
                  style: AppTypography.bodyS.copyWith(color: kc.text2),
                ),
                TextButton(
                  onPressed: _isLoading
                      ? null
                      : () => context.go(_authLocation('/auth/login')),
                  child: const Text('Giriş Yap'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  String _authLocation(String path) {
    final target = widget.returnTo;
    if (target == null || target.isEmpty || target.startsWith('/auth/')) {
      return path;
    }
    return '$path?returnTo=${Uri.encodeQueryComponent(target)}';
  }
}
