import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_colors.dart';
import 'admin_service.dart';

class AdminLoginScreen extends StatefulWidget {
  const AdminLoginScreen({super.key, required this.adminService});
  final AdminService adminService;

  @override
  State<AdminLoginScreen> createState() => _AdminLoginScreenState();
}

class _AdminLoginScreenState extends State<AdminLoginScreen> {
  final _emailCtrl = TextEditingController();
  final _passwordCtrl = TextEditingController();
  final _totpCtrl = TextEditingController();
  final _formKey = GlobalKey<FormState>();

  bool _loading = false;
  bool _showTotp = false;
  String? _error;

  @override
  void dispose() {
    _emailCtrl.dispose();
    _passwordCtrl.dispose();
    _totpCtrl.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() { _loading = true; _error = null; });
    try {
      await widget.adminService.login(
        email: _emailCtrl.text.trim(),
        password: _passwordCtrl.text,
        totpCode: _totpCtrl.text.trim(),
      );
      if (mounted) context.go('/admin');
    } catch (e) {
      setState(() {
        _error = e.toString().contains('INVALID_TOTP')
            ? 'Geçersiz TOTP kodu.'
            : e.toString().contains('INVALID_CREDENTIALS')
                ? 'E-posta veya şifre hatalı.'
                : 'Giriş başarısız. Tekrar dene.';
      });
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0F172A),
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 400),
            child: Form(
              key: _formKey,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(Icons.shield_rounded, size: 56, color: AppColors.primary),
                  const SizedBox(height: 16),
                  Text(
                    'Admin Paneli',
                    style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                          color: Colors.white,
                          fontWeight: FontWeight.w800,
                        ),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'karar.app yönetim paneli',
                    style: TextStyle(color: Colors.white.withValues(alpha: 0.5), fontSize: 13),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 40),
                  _Field(
                    controller: _emailCtrl,
                    label: 'E-posta',
                    icon: Icons.email_outlined,
                    keyboardType: TextInputType.emailAddress,
                    validator: (v) =>
                        (v == null || !v.contains('@')) ? 'Geçerli e-posta gir' : null,
                  ),
                  const SizedBox(height: 16),
                  _Field(
                    controller: _passwordCtrl,
                    label: 'Şifre',
                    icon: Icons.lock_outline,
                    obscure: true,
                    validator: (v) =>
                        (v == null || v.isEmpty) ? 'Şifre boş olamaz' : null,
                  ),
                  const SizedBox(height: 16),
                  if (!_showTotp)
                    TextButton(
                      onPressed: () => setState(() => _showTotp = true),
                      child: const Text(
                        'TOTP kodu gir (Google Authenticator)',
                        style: TextStyle(color: AppColors.primary),
                      ),
                    )
                  else
                    _Field(
                      controller: _totpCtrl,
                      label: '6 haneli TOTP kodu',
                      icon: Icons.pin_outlined,
                      keyboardType: TextInputType.number,
                      validator: (v) =>
                          (v == null || v.length != 6) ? '6 haneli kod gir' : null,
                    ),
                  if (_error != null) ...[
                    const SizedBox(height: 16),
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: AppColors.haksiz.withValues(alpha: 0.15),
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Text(
                        _error!,
                        style: const TextStyle(color: AppColors.haksiz, fontSize: 13),
                        textAlign: TextAlign.center,
                      ),
                    ),
                  ],
                  const SizedBox(height: 24),
                  FilledButton(
                    onPressed: _loading ? null : _submit,
                    style: FilledButton.styleFrom(
                      backgroundColor: AppColors.primary,
                      padding: const EdgeInsets.symmetric(vertical: 16),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
                    ),
                    child: _loading
                        ? const SizedBox(
                            width: 20, height: 20,
                            child: CircularProgressIndicator(
                                strokeWidth: 2, color: Colors.white),
                          )
                        : const Text('Giriş Yap',
                            style: TextStyle(fontWeight: FontWeight.w700, fontSize: 15)),
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

class _Field extends StatefulWidget {
  const _Field({
    required this.controller,
    required this.label,
    required this.icon,
    this.obscure = false,
    this.keyboardType,
    this.validator,
  });

  final TextEditingController controller;
  final String label;
  final IconData icon;
  final bool obscure;
  final TextInputType? keyboardType;
  final String? Function(String?)? validator;

  @override
  State<_Field> createState() => _FieldState();
}

class _FieldState extends State<_Field> {
  late bool _obscure = widget.obscure;

  @override
  Widget build(BuildContext context) {
    return TextFormField(
      controller: widget.controller,
      obscureText: _obscure,
      keyboardType: widget.keyboardType,
      validator: widget.validator,
      style: const TextStyle(color: Colors.white),
      decoration: InputDecoration(
        labelText: widget.label,
        labelStyle: TextStyle(color: Colors.white.withValues(alpha: 0.6)),
        prefixIcon: Icon(widget.icon, color: Colors.white.withValues(alpha: 0.4)),
        suffixIcon: widget.obscure
            ? IconButton(
                icon: Icon(_obscure ? Icons.visibility_off : Icons.visibility,
                    color: Colors.white.withValues(alpha: 0.4)),
                onPressed: () => setState(() => _obscure = !_obscure),
              )
            : null,
        filled: true,
        fillColor: Colors.white.withValues(alpha: 0.07),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: BorderSide(color: Colors.white.withValues(alpha: 0.1)),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: BorderSide(color: Colors.white.withValues(alpha: 0.1)),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: const BorderSide(color: AppColors.primary),
        ),
        errorStyle: const TextStyle(color: AppColors.haksiz),
      ),
    );
  }
}
