import 'dart:async';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_service.dart';
import '../../core/theme/app_colors.dart';
import '../../core/utils/validators.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/karar_logo.dart';

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
        return const Icon(Icons.check_circle, color: AppColors.hakli);
      case UsernameStatus.taken:
        return const Icon(Icons.cancel, color: AppColors.haksiz);
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
      lastDate: DateTime(now.year - 13), // 13+ limit
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

    if (!_acceptedPolicy) {
      setState(
        () => _error =
            'Kullanim kosullari ve topluluk kurallarini kabul etmelisin.',
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
        title: const Text('Hesap Oluştur'),
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
                      Icons.person_add_rounded,
                      size: 64,
                      color: colorScheme.primary,
                    ),
                  ),
                  const SizedBox(height: 24),
                  Text(
                    'Topluluğa Katıl',
                    style: textTheme.headlineMedium?.copyWith(
                      fontWeight: FontWeight.bold,
                    ),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Kaydol, fikrini söyle ve adaleti sağla.',
                    style: textTheme.bodyMedium?.copyWith(
                      color: colorScheme.onSurfaceVariant,
                    ),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 24),
                  Container(
                    padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(
                      color:
                          colorScheme.secondaryContainer.withValues(alpha: 0.4),
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Row(
                      children: [
                        Icon(Icons.info_outline,
                            size: 20, color: colorScheme.secondary),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Text(
                            'Lütfen bilgileri doğru girin. Doğum tarihi ve cinsiyetiniz ileride hesap kurtarma işlemlerinde gerekebilir.',
                            style: textTheme.bodySmall?.copyWith(height: 1.3),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 32),
                  TextFormField(
                    controller: _usernameCtrl,
                    decoration: InputDecoration(
                      labelText: 'Kullanıcı adı',
                      prefixIcon: const Icon(Icons.alternate_email),
                      border: const OutlineInputBorder(),
                      helperText: _usernameStatus == UsernameStatus.available
                          ? 'Kullanılabilir'
                          : _usernameStatus == UsernameStatus.taken
                              ? 'Bu kullanıcı adı alınmış'
                              : '3-20 karakter, harf/rakam/alt çizgi',
                      helperStyle: TextStyle(
                        color: _usernameStatus == UsernameStatus.available
                            ? AppColors.hakli
                            : _usernameStatus == UsernameStatus.taken
                                ? AppColors.haksiz
                                : null,
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
                  const SizedBox(height: 20),
                  TextFormField(
                    controller: _emailCtrl,
                    decoration: const InputDecoration(
                      labelText: 'E-posta',
                      prefixIcon: Icon(Icons.email_outlined),
                      border: OutlineInputBorder(),
                    ),
                    keyboardType: TextInputType.emailAddress,
                    textInputAction: TextInputAction.next,
                    validator: Validators.email,
                  ),
                  const SizedBox(height: 20),
                  TextFormField(
                    controller: _dobCtrl,
                    readOnly: true,
                    onTap: _selectDob,
                    decoration: const InputDecoration(
                      labelText: 'Doğum Tarihi',
                      prefixIcon: Icon(Icons.calendar_today_outlined),
                      border: OutlineInputBorder(),
                      hintText: 'Gün/Ay/Yıl',
                    ),
                    validator: (v) =>
                        v == null || v.isEmpty ? 'Doğum tarihi gerekli.' : null,
                  ),
                  const SizedBox(height: 20),
                  DropdownButtonFormField<String>(
                    initialValue: _selectedGender,
                    decoration: const InputDecoration(
                      labelText: 'Cinsiyet',
                      prefixIcon: Icon(Icons.people_outline),
                      border: OutlineInputBorder(),
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
                    validator: (v) =>
                        v == null ? 'Cinsiyet seçimi gerekli.' : null,
                  ),
                  const SizedBox(height: 20),
                  TextFormField(
                    controller: _passwordCtrl,
                    onFieldSubmitted: (_) =>
                        _confirmPasswordFocus.requestFocus(),
                    decoration: InputDecoration(
                      labelText: 'Şifre',
                      prefixIcon: const Icon(Icons.lock_outline),
                      border: const OutlineInputBorder(),
                      helperText: 'En az 8 karakter',
                      suffixIcon: IconButton(
                        onPressed: () => setState(() => _obscure = !_obscure),
                        icon: Icon(
                          _obscure ? Icons.visibility_off : Icons.visibility,
                        ),
                      ),
                    ),
                    obscureText: _obscure,
                    textInputAction: TextInputAction.next,
                    validator: Validators.password,
                  ),
                  const SizedBox(height: 20),
                  TextFormField(
                    controller: _confirmPasswordCtrl,
                    focusNode: _confirmPasswordFocus,
                    decoration: InputDecoration(
                      labelText: 'Şifre Tekrar',
                      prefixIcon: const Icon(Icons.lock_reset_outlined),
                      border: const OutlineInputBorder(),
                      suffixIcon: IconButton(
                        onPressed: () =>
                            setState(() => _obscureConfirm = !_obscureConfirm),
                        icon: Icon(
                          _obscureConfirm
                              ? Icons.visibility_off
                              : Icons.visibility,
                        ),
                      ),
                    ),
                    obscureText: _obscureConfirm,
                    textInputAction: TextInputAction.done,
                    onFieldSubmitted: (_) => _submit(),
                    validator: (v) =>
                        Validators.passwordsMatch(v, _passwordCtrl.text),
                  ),
                  const SizedBox(height: 16),
                  CheckboxListTile(
                    value: _acceptedPolicy,
                    onChanged: _isLoading
                        ? null
                        : (value) => setState(
                              () => _acceptedPolicy = value ?? false,
                            ),
                    controlAffinity: ListTileControlAffinity.leading,
                    contentPadding: EdgeInsets.zero,
                    title: const Text(
                      'Kullanim Kosullari ve Topluluk Kurallarini kabul ediyorum.',
                    ),
                  ),
                  Wrap(
                    alignment: WrapAlignment.center,
                    spacing: 8,
                    children: [
                      TextButton(
                        onPressed: () => context.push('/legal/terms'),
                        child: const Text('Kullanim Kosullari'),
                      ),
                      TextButton(
                        onPressed: () => context.push('/legal/community'),
                        child: const Text('Topluluk Kurallari'),
                      ),
                    ],
                  ),
                  if (_error != null) ...[
                    const SizedBox(height: 16),
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color:
                            colorScheme.errorContainer.withValues(alpha: 0.3),
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
                    label: 'Kaydol',
                    onPressed:
                        _usernameStatus == UsernameStatus.taken || _isLoading
                            ? null
                            : _submit,
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
                    label: 'Google ile Kaydol',
                    onPressed: _isLoading ? null : _googleSignIn,
                    variant: KararButtonVariant.outlined,
                    icon: Icons.g_mobiledata,
                  ),
                  const SizedBox(height: 32),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      const Text('Zaten hesabın var mı?'),
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
          ),
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
