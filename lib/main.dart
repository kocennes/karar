import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_crashlytics/firebase_crashlytics.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_web_plugins/url_strategy.dart';

import 'app.dart';
import 'core/ads/ad_service.dart';
import 'core/app_services.dart';
import 'core/providers.dart';
import 'core/theme/app_theme.dart';
import 'core/theme/karar_tokens.dart';
import 'firebase_options.dart';
import 'shared/widgets/offline_view.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  usePathUrlStrategy();
  LicenseRegistry.addLicense(() async* {
    final text = await rootBundle.loadString(
      'assets/fonts/plus_jakarta_sans/OFL.txt',
    );
    yield LicenseEntryWithLineBreaks(['Plus Jakarta Sans'], text);
  });

  // Paint the bootstrap screen before waiting on anything network-bound: on
  // web Firebase.initializeApp first downloads the Firebase JS SDK from
  // www.gstatic.com, and awaiting it here left a blank page for as long as
  // that took. _BootstrapApp still awaits it before creating the app services,
  // so the initialisation order is unchanged.
  runApp(_BootstrapApp(platformInit: _initPlatformServices()));
}

/// Firebase (+ error handlers) and AdMob. Never throws.
Future<void> _initPlatformServices() async {
  try {
    await Firebase.initializeApp(
      options: DefaultFirebaseOptions.currentPlatform,
    );

    if (kIsWeb) {
      FlutterError.onError = (details) {
        debugPrint('[Flutter Web Error] ${details.exceptionAsString()}\n${details.stack}');
      };
      PlatformDispatcher.instance.onError = (error, stack) {
        debugPrint('[Flutter Web Uncaught] $error\n$stack');
        return true;
      };
    } else {
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
}

class _BootstrapApp extends StatefulWidget {
  const _BootstrapApp({required this.platformInit});

  /// [_initPlatformServices], started before the first frame.
  final Future<void> platformInit;

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
      await widget.platformInit;
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
      // Same brand panel as the splash screen that follows, so the hand-off
      // from bootstrap to splash does not flash a flat block.
      return MaterialApp(
        debugShowCheckedModeBanner: false,
        home: Scaffold(
          backgroundColor: KararColors.light.primaryFill,
          body: DecoratedBox(
            decoration: const BoxDecoration(gradient: KararGradients.brand),
            child: Center(
              child: CircularProgressIndicator(
                color: KararColors.light.onColor,
              ),
            ),
          ),
        ),
      );
    }

    if (_error != null || _services == null) {
      // Bundled-font themes: the default (Roboto) is fetched from
      // fonts.gstatic.com on web, so this "no connection" text would itself
      // need the network to render.
      return MaterialApp(
        debugShowCheckedModeBanner: false,
        theme: AppTheme.light(),
        darkTheme: AppTheme.dark(),
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
