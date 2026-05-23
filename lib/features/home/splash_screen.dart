import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/notifications/notification_permission_dialog.dart';
import '../../core/providers.dart';
import '../../core/theme/app_colors.dart';
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
    return Scaffold(
      backgroundColor: AppColors.primary,
      body: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(
              Icons.gavel_rounded,
              size: 80,
              color: Colors.white,
            ),
            const SizedBox(height: 24),
            Text(
              'karar',
              style: Theme.of(context).textTheme.headlineLarge?.copyWith(
                    color: Colors.white,
                    fontWeight: FontWeight.w900,
                    letterSpacing: -1,
                  ),
            ),
          ],
        ),
      ),
    );
  }
}
