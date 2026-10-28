import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/notifications/notification_permission_dialog.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/update/force_update_dialog.dart';
import '../onboarding/onboarding_screen.dart';

class SplashScreen extends ConsumerStatefulWidget {
  const SplashScreen({super.key});

  @override
  ConsumerState<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends ConsumerState<SplashScreen> {
  @override
  void initState() {
    super.initState();
    _navigate();
  }

  Future<void> _navigate() async {
    await Future<void>.delayed(const Duration(seconds: 1));
    if (!mounted) return;

    // Zorunlu güncelleme kontrolü (F14)
    final updateService = ref.read(forceUpdateServiceProvider);
    final versionInfo = await updateService.checkForUpdate();
    if (!mounted) return;
    await ForceUpdateDialog.showIfNeeded(context, versionInfo);
    if (!mounted) return;

    final onboardingDone = await isOnboardingDone();
    if (!mounted) return;

    if (!onboardingDone) {
      context.go('/onboarding');
      return;
    }

    // Politika versiyonu kontrolü — sadece giriş yapmış kullanıcılar için
    final authService = ref.read(authServiceProvider);
    if (authService.isLoggedIn) {
      try {
        final policyStatus = await authService.checkPolicyStatus();
        if (!mounted) return;
        if (policyStatus.needsAcceptance) {
          await context.push<void>(
            '/policy-acceptance',
            extra: (policyStatus, authService),
          );
          if (!mounted) return;
        }
      } catch (_) {
        // Politika kontrolü başarısız olursa kullanıcıyı bloklama
      }
    }

    // 3. açılışta bildirim izni ön dialogu (F13)
    final sessionTracker = ref.read(sessionTrackerProvider);
    if (sessionTracker.sessionNumber == 3) {
      await NotificationPermissionDialog.showIfNeeded(
        context,
        notificationService: ref.read(notificationServiceProvider),
      );
      if (!mounted) return;
    }

    // Deep link desteği: Eğer kullanıcı bir alt sayfaya (örn: /posts/123)
    // doğrudan gelmişse, onu ana sayfaya zorla yönlendirme.
    final state = GoRouterState.of(context);
    if (state.uri.path == '/splash') {
      context.go('/');
    }
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    // Brand panel in both themes (the violet → deep-violet gradient, so the
    // dark theme does not open on a flat bright block), with the logo's
    // balance mark.
    return Scaffold(
      backgroundColor: kc.primaryFill,
      body: DecoratedBox(
        decoration: const BoxDecoration(gradient: KararGradients.brand),
        child: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ExcludeSemantics(
                child: Icon(
                  Icons.balance_rounded,
                  size: 80,
                  color: kc.onColor,
                ),
              ),
              const SizedBox(height: 24),
              Text(
                'karar',
                style: AppTypography.display.copyWith(
                  fontSize: 40,
                  height: 1.1,
                  color: kc.onColor,
                  letterSpacing: -1,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
