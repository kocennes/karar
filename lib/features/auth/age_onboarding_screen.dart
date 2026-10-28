import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/auth/age_gate.dart';
import '../../core/auth/auth_service.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/widgets/karar_button.dart';
import 'widgets/age_gate_widgets.dart';
import 'widgets/auth_layout.dart';

/// P0-02: neutral birth-date + terms step for accounts that have no birth
/// date yet (`requiresOnboarding`: Google sign-up, old accounts). Calls
/// `POST /api/v1/users/me/onboarding`. Pops `true` when done; an ineligible
/// date locks the device, ends the session and shows the neutral refusal.
/// Going back abandons the sign-in (the session is closed).
class AgeOnboardingScreen extends StatefulWidget {
  const AgeOnboardingScreen({super.key, required this.authService});

  final AuthService authService;

  @override
  State<AgeOnboardingScreen> createState() => _AgeOnboardingScreenState();
}

class _AgeOnboardingScreenState extends State<AgeOnboardingScreen> {
  final _formKey = GlobalKey<FormState>();
  DateTime? _dob;
  bool _accepted = false;
  bool _isLoading = false;
  String? _error;

  /// null until the device lock is read.
  bool? _locked;

  @override
  void initState() {
    super.initState();
    _readLock();
  }

  Future<void> _readLock() async {
    var locked = false;
    try {
      locked = await widget.authService.isAgeGateLocked();
    } catch (_) {}
    if (locked) await _endSession();
    if (mounted) setState(() => _locked = locked);
  }

  Future<void> _endSession() async {
    if (!widget.authService.isLoggedIn) return;
    try {
      await widget.authService.logout();
    } catch (_) {}
  }

  Future<void> _refuse() async {
    try {
      await widget.authService.lockAgeGate();
    } catch (_) {}
    await _endSession();
    if (!mounted) return;
    setState(() {
      _locked = true;
      _isLoading = false;
      _error = null;
    });
  }

  Future<void> _leave() async {
    await _endSession();
    if (mounted) context.go('/');
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    if (!_accepted) {
      setState(
        () => _error =
            'Kullanım koşulları ve topluluk kurallarını kabul etmelisin.',
      );
      return;
    }

    final dob = _dob!;
    final eligible = AgeGate.isEligible(dob);
    setState(() {
      _isLoading = true;
      _error = null;
    });
    // Lock first so the gate stays closed even if the request fails.
    if (!eligible) {
      try {
        await widget.authService.lockAgeGate();
      } catch (_) {}
    }

    try {
      // Sent for an ineligible date too: the server locks the device and
      // revokes the account's sessions; it never stores that date.
      await widget.authService.completeOnboarding(
        dateOfBirth: dob,
        acceptedTerms: true,
        acceptedCommunityGuidelines: true,
      );
    } catch (e) {
      if (!eligible ||
          (e is ApiException && e.code == AgeGate.notEligibleCode)) {
        await _refuse();
        return;
      }
      if (!mounted) return;
      setState(() {
        _isLoading = false;
        _error =
            e is ApiException ? e.message : 'Bir şey ters gitti. Tekrar dene.';
      });
      return;
    }

    if (!eligible) {
      await _refuse();
      return;
    }
    if (!mounted) return;
    if (context.canPop()) {
      context.pop(true);
    } else {
      context.go('/');
    }
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final Widget body;
    if (_locked == null) {
      body = const SizedBox.shrink();
    } else if (_locked!) {
      body = const AgeGateRefusal();
    } else {
      body = Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const AuthHeader(
              title: 'Bir adım kaldı',
              subtitle: Text('Devam etmek için doğum tarihini gir.'),
            ),
            BirthDateField(
              value: _dob,
              enabled: !_isLoading,
              onChanged: (picked) => setState(() => _dob = picked),
            ),
            const SizedBox(height: 16),
            CheckboxListTile(
              value: _accepted,
              onChanged: _isLoading
                  ? null
                  : (value) => setState(() => _accepted = value ?? false),
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
              label: 'Devam et',
              size: KararButtonSize.large,
              onPressed: _isLoading ? null : _submit,
              isLoading: _isLoading,
            ),
            const SizedBox(height: 12),
            TextButton(
              onPressed: _isLoading ? null : _leave,
              child: const Text('Vazgeç ve çıkış yap'),
            ),
          ],
        ),
      );
    }

    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop && !_isLoading) _leave();
      },
      child: AuthScaffold(child: body),
    );
  }
}
