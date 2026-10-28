import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_service.dart';
import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/centered_content.dart';

class EditProfileScreen extends ConsumerStatefulWidget {
  const EditProfileScreen({super.key, this.user});

  final AuthUser? user;

  @override
  ConsumerState<EditProfileScreen> createState() => _EditProfileScreenState();
}

class _EditProfileScreenState extends ConsumerState<EditProfileScreen> {
  late final TextEditingController _usernameCtrl;
  late final TextEditingController _bioCtrl;
  bool _saving = false;
  String? _usernameError;

  static const int _bioMaxLength = 150;

  @override
  void initState() {
    super.initState();
    final user = widget.user ?? ref.read(currentUserProvider);
    _usernameCtrl = TextEditingController(text: user?.username ?? '');
    _bioCtrl = TextEditingController(text: user?.bio ?? '');
  }

  @override
  void dispose() {
    _usernameCtrl.dispose();
    _bioCtrl.dispose();
    super.dispose();
  }

  bool get _usernameChanged =>
      _usernameCtrl.text.trim() != _currentUser?.username;

  AuthUser? get _currentUser => widget.user ?? ref.read(currentUserProvider);

  Future<void> _save() async {
    final user = _currentUser;
    if (user == null) return;

    final newUsername = _usernameChanged ? _usernameCtrl.text.trim() : null;
    final newBio = _bioCtrl.text.trim();
    final bioChanged = newBio != (user.bio ?? '');

    if (newUsername == null && !bioChanged) {
      Navigator.pop(context);
      return;
    }

    // Username cooldown check
    if (newUsername != null && !user.canChangeUsername) {
      setState(() =>
          _usernameError = 'Kullanıcı adını 30 günde bir değiştirebilirsin.');
      return;
    }

    setState(() {
      _saving = true;
      _usernameError = null;
    });

    try {
      final authService = ref.read(authServiceProvider);

      // Check availability before saving
      if (newUsername != null) {
        final available = await authService.isUsernameAvailable(newUsername);
        if (!available) {
          if (mounted) {
            setState(() {
              _saving = false;
              _usernameError = 'Bu kullanıcı adı zaten alınmış.';
            });
          }
          return;
        }
      }

      await authService.updateProfile(
        username: newUsername,
        bio: bioChanged ? newBio : null,
      );

      final updatedUser = authService.currentUser;
      if (updatedUser != null) {
        ref.read(currentUserProvider.notifier).state = updatedUser;
      }

      if (mounted) Navigator.pop(context);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(e.toString())),
        );
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final user = ref.watch(currentUserProvider) ?? widget.user;
    if (user == null) {
      return Scaffold(
        appBar: AppBar(
          title: const Text('Profili Düzenle'),
          centerTitle: true,
        ),
        body: CenteredContent(
          maxWidth: 420,
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                ExcludeSemantics(
                  child: Container(
                    width: 88,
                    height: 88,
                    decoration: BoxDecoration(
                      color: kc.primaryContainer,
                      shape: BoxShape.circle,
                    ),
                    child: Icon(
                      Icons.account_circle_rounded,
                      size: 40,
                      color: kc.primary,
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                Text(
                  'Profilini düzenlemek için giriş yapmalısın.',
                  textAlign: TextAlign.center,
                  style: AppTypography.bodyStrong.copyWith(
                    fontSize: 16,
                    color: kc.text1,
                  ),
                ),
                const SizedBox(height: 20),
                KararButton(
                  label: 'Giriş yap',
                  expand: false,
                  onPressed: () =>
                      context.push('/auth/login?returnTo=/profile/edit'),
                ),
              ],
            ),
          ),
        ),
      );
    }

    final canChangeUsername = user.canChangeUsername;
    final changedAt = user.usernameChangedAt;
    final labelStyle = AppTypography.labelS.copyWith(
      fontWeight: FontWeight.w800,
      color: kc.text2,
    );

    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, result) async {
        if (didPop) return;

        final bioChanged = _bioCtrl.text.trim() != (user.bio ?? '');

        if (!_usernameChanged && !bioChanged) {
          if (context.mounted) Navigator.pop(context);
          return;
        }

        final shouldPop = await showDialog<bool>(
          context: context,
          builder: (ctx) => AlertDialog(
            title: const Text('Değişiklikleri Kaydetme?'),
            content: const Text(
                'Yaptığın değişiklikler kaydedilmeyecek. Çıkmak istiyor musun?'),
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
          title: const Text('Profili Düzenle'),
          centerTitle: true,
        ),
        body: CenteredContent(
          maxWidth: 500,
          child: ListView(
            padding: EdgeInsets.fromLTRB(
                24, 8, 24, context.navBottomInset + 24),
            children: [
              Center(
                child: Column(
                  children: [
                    KararAvatar(
                      username: user.username,
                      radius: 48,
                      fontSize: 38,
                      ring: KararAvatarRing.gradient,
                    ),
                    const SizedBox(height: 8),
                    const TextButton(
                      onPressed: null, // fotoğraf yükleme gelecek fazda
                      child: Text('Fotoğrafı Değiştir'),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 20),
              Text('Kullanıcı adı', style: labelStyle),
              const SizedBox(height: 8),
              TextField(
                controller: _usernameCtrl,
                enabled: canChangeUsername,
                decoration: InputDecoration(
                  errorText: _usernameError,
                  prefixIcon: const Icon(Icons.alternate_email_rounded),
                  helperText: canChangeUsername
                      ? '3-20 karakter, harf/rakam/alt çizgi'
                      : changedAt != null
                          ? 'Son değişim: ${_formatDate(changedAt)}'
                          : null,
                ),
                onChanged: (_) {
                  if (_usernameError != null) {
                    setState(() => _usernameError = null);
                  }
                },
              ),
              if (!canChangeUsername) ...[
                const SizedBox(height: 6),
                Text(
                  'Kullanıcı adını 30 günde bir değiştirebilirsin.',
                  style: AppTypography.meta.copyWith(color: kc.haksizText),
                ),
              ],
              const SizedBox(height: 24),
              Text('Biyografi', style: labelStyle),
              const SizedBox(height: 8),
              ValueListenableBuilder<TextEditingValue>(
                valueListenable: _bioCtrl,
                builder: (_, value, __) => TextField(
                  controller: _bioCtrl,
                  maxLines: 4,
                  maxLength: _bioMaxLength,
                  decoration: InputDecoration(
                    hintText: 'Kendini topluluğa tanıt...',
                    alignLabelWithHint: true,
                    counterText: '${value.text.length}/$_bioMaxLength',
                  ),
                ),
              ),
              const SizedBox(height: 24),
              KararButton(
                label: 'Kaydet',
                size: KararButtonSize.large,
                isLoading: _saving,
                onPressed: _saving ? null : _save,
              ),
            ],
          ),
        ),
      ),
    );
  }

  String _formatDate(DateTime dt) => '${dt.day}/${dt.month}/${dt.year}';
}
