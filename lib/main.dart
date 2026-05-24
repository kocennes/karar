import 'dart:ui';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_crashlytics/firebase_crashlytics.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_web_plugins/url_strategy.dart';

import 'app.dart';
import 'core/ads/ad_service.dart';
import 'core/app_services.dart';
import 'core/providers.dart';
import 'firebase_options.dart';
import 'shared/widgets/offline_view.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  usePathUrlStrategy();

  try {
    await Firebase.initializeApp(
      options: DefaultFirebaseOptions.currentPlatform,
    );

    if (!kIsWeb) {
      FlutterError.onError =
          FirebaseCrashlytics.instance.recordFlutterFatalError;
      PlatformDispatcher.instance.onError = (error, stack) {
        FirebaseCrashlytics.instance.recordError(error, stack, fatal: true);
        return true;
      };
    }
  } catch (e) {
    debugPrint(
        'Firebase başlatılamadı: $e\nLütfen google-services.json dosyasını ekleyin.');
  }

  try {
    await AdService.instance.init();
  } catch (e) {
    debugPrint('AdMob başlatılamadı: $e');
  }

  runApp(const _BootstrapApp());
}

class _BootstrapApp extends StatefulWidget {
  const _BootstrapApp();

  @override
  State<_BootstrapApp> createState() => _BootstrapAppState();
}

class _BootstrapAppState extends State<_BootstrapApp> {
  AppServices? _services;
  String? _error;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _init();
  }

  Future<void> _init() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final services = await AppServices.create();
      setState(() {
        _services = services;
        _loading = false;
      });
    } catch (e) {
      setState(() {
        _error = e.toString();
        _loading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const MaterialApp(
        debugShowCheckedModeBanner: false,
        home: Scaffold(
          backgroundColor: Color(0xFF6366F1), // AppColors.primary
          body: Center(
            child: CircularProgressIndicator(color: Colors.white),
          ),
        ),
      );
    }

    if (_error != null || _services == null) {
      return MaterialApp(
        debugShowCheckedModeBanner: false,
        home: OfflineView(onRetry: _init),
      );
    }

    return ProviderScope(
      overrides: [
        apiClientProvider.overrideWithValue(_services!.apiClient),
        remoteConfigProvider.overrideWithValue(_services!.remoteConfig),
        authServiceProvider.overrideWithValue(_services!.authService),
        shareServiceProvider.overrideWithValue(_services!.shareService),
        postRepositoryProvider.overrideWithValue(_services!.postRepository),
        notificationRepositoryProvider
            .overrideWithValue(_services!.notificationRepository),
        analyticsServiceProvider.overrideWithValue(_services!.analyticsService),
        ratingServiceProvider.overrideWithValue(_services!.ratingService),
        notificationServiceProvider
            .overrideWithValue(_services!.notificationService),
        sessionTrackerProvider.overrideWithValue(_services!.sessionTracker),
        forceUpdateServiceProvider
            .overrideWithValue(_services!.forceUpdateService),
        currentUserProvider
            .overrideWith((ref) => _services!.authService.currentUser),
        pendingEmailChangeProvider
            .overrideWith((ref) => _services!.authService.pendingEmailChange),
      ],
      child: KararApp(services: _services!),
    );
  }
}
