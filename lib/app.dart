import 'dart:async';

import 'package:firebase_crashlytics/firebase_crashlytics.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/app_services.dart';
import 'core/auth/auth_service.dart';
import 'core/config/remote_config_service.dart';
import 'core/keyboard/app_shortcuts.dart';
import 'core/providers.dart';
import 'core/router/app_router.dart';
import 'core/theme/app_theme.dart';
import 'core/theme/font_size_provider.dart';
import 'core/theme/theme_provider.dart';
import 'core/utils/sw_update_service.dart';
import 'core/web/tab_auth_sync.dart';
import 'features/feed/feed_provider.dart';
import 'features/notifications/notifications_provider.dart';
import 'shared/widgets/maintenance_screen.dart';

class KararApp extends ConsumerStatefulWidget {
  const KararApp({super.key, required this.services});

  final AppServices services;

  @override
  ConsumerState<KararApp> createState() => _KararAppState();
}

class _KararAppState extends ConsumerState<KararApp>
    with WidgetsBindingObserver {
  late final _router = buildRouter(widget.services);
  StreamSubscription<RemoteMessage>? _sub;
  StreamSubscription<bool>? _tabAuthSub;
  DateTime? _backgroundedAt;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    try {
      _sub = FirebaseMessaging.onMessageOpenedApp.listen(_onMessageOpened);
      _checkInitialMessage();
      _checkPreviousCrash();
    } catch (_) {}

    widget.services.authService.setOnLogout((reason) {
      if (reason == LogoutReason.sessionExpired) {
        ref.read(logoutReasonProvider.notifier).state =
            LogoutReason.sessionExpired;
      }
      ref.read(currentUserProvider.notifier).state = null;
    });

    widget.services.apiClient.setOnMaintenance(() {
      ref.read(maintenanceProvider.notifier).state = true;
    });

    // Remote Config'den bakım modu kontrolü — API 503 gelmeden önce de devreye girer
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final rc = ref.read(remoteConfigProvider);
      if (rc.getBool(RemoteConfigKeys.maintenanceMode)) {
        ref.read(maintenanceProvider.notifier).state = true;
      }
    });

    // W34: service worker update banner
    SwUpdateService.listenForUpdates(_onSwUpdate);

    // W36: multi-tab auth sync — diğer sekmede login/logout olunca bu sekmeyi senkronize et
    _tabAuthSub = TabAuthSync.authChanges.listen((loggedIn) async {
      if (loggedIn) {
        await widget.services.authService.init();
      } else {
        widget.services.authService.logout(reason: LogoutReason.manual);
      }
      if (mounted) {
        ref.read(currentUserProvider.notifier).state =
            widget.services.authService.currentUser;
      }
    });
  }

  void _onSwUpdate() {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: const Text('Yeni sürüm hazır.'),
        duration: const Duration(seconds: 0),
        behavior: SnackBarBehavior.floating,
        action: SnackBarAction(
          label: 'Yenile',
          onPressed: () {
            // Web'de sayfayı yenile; native'de no-op
            _reloadPage();
          },
        ),
      ),
    );
  }

  // W34: web'de reload, native'de no-op
  void _reloadPage() => SwUpdateService.reload();

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _sub?.cancel();
    _tabAuthSub?.cancel();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused ||
        state == AppLifecycleState.detached) {
      _backgroundedAt = DateTime.now();
      _flushSession();
    } else if (state == AppLifecycleState.resumed) {
      _onResumed();
    }
  }

  void _onResumed() {
    widget.services.authService.init();

    final bg = _backgroundedAt;
    if (bg != null &&
        DateTime.now().difference(bg) >= const Duration(minutes: 5)) {
      ref.read(feedProvider.notifier).refresh(silent: true);
      ref.invalidate(notificationsProvider);
    }
    _backgroundedAt = null;
  }

  void _flushSession() {
    final tracker = ref.read(sessionTrackerProvider);
    final stats = tracker.flush();
    if (stats.durationSeconds < 2) return;
    ref.read(analyticsServiceProvider).logSessionEnd(
          durationSeconds: stats.durationSeconds,
          postsViewed: stats.postsViewed,
          votesCast: stats.votesCast,
          commentsPosted: stats.commentsPosted,
        );
  }

  Future<void> _checkInitialMessage() async {
    final message = await FirebaseMessaging.instance.getInitialMessage();
    if (message != null) _onMessageOpened(message);
  }

  Future<void> _checkPreviousCrash() async {
    if (kIsWeb) return;
    try {
      final didCrash =
          await FirebaseCrashlytics.instance.didCrashOnPreviousExecution();
      if (didCrash && mounted) {
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (!mounted) return;
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content:
                  Text('Bir sorun yaşandı. Kaldığın yerden devam edebilirsin.'),
              duration: Duration(seconds: 4),
            ),
          );
        });
      }
    } catch (_) {}
  }

  void _onMessageOpened(RemoteMessage message) {
    final postId = message.data['postId'] ?? message.data['referenceId'];
    if (postId != null) {
      _router.go('/posts/$postId');
    } else {
      _router.go('/notifications');
    }
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(logoutReasonProvider, (previous, next) {
      if (next == LogoutReason.sessionExpired) {
        final returnTo = _router.routerDelegate.currentConfiguration.uri;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: const Text(
              'Oturum süren doldu. Tekrar giriş yap.',
            ),
            behavior: SnackBarBehavior.floating,
            action: SnackBarAction(
              label: 'Giriş Yap',
              onPressed: () {
                final target = returnTo.toString();
                final location = target.startsWith('/auth/')
                    ? '/auth/login'
                    : '/auth/login?returnTo=${Uri.encodeQueryComponent(target)}';
                _router.push(location);
              },
            ),
          ),
        );
        ref.read(logoutReasonProvider.notifier).state = null;
      }
    });

    ref.listen(currentUserProvider, (previous, next) {
      ref.read(analyticsServiceProvider).setUserType(next != null);
    });

    final themeMode = ref.watch(themeProvider);
    final fontSize = ref.watch(fontSizeProvider);
    final isMaintenance = ref.watch(maintenanceProvider);

    return AppShortcuts(
      child: MaterialApp.router(
        title: 'Karar',
        debugShowCheckedModeBanner: false,
        theme: AppTheme.light(),
        darkTheme: AppTheme.dark(),
        themeMode: themeMode,
        routerConfig: _router,
        builder: (context, child) {
          final scaled = MediaQuery(
            data: MediaQuery.of(context).copyWith(
              textScaler: TextScaler.linear(fontSize.factor),
            ),
            child: child!,
          );
          if (isMaintenance) {
            return MediaQuery(
              data: MediaQuery.of(context).copyWith(
                textScaler: TextScaler.linear(fontSize.factor),
              ),
              child: MaintenanceScreen(
                onRetry: () =>
                    ref.read(maintenanceProvider.notifier).state = false,
              ),
            );
          }
          return scaled;
        },
      ),
    );
  }
}
