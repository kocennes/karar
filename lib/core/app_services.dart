import '../features/feed/post_repository.dart';
import 'update/force_update_service.dart';
import '../features/notifications/data/notification_repository.dart';
import 'analytics/analytics_service.dart';
import 'analytics/session_tracker.dart';
import 'api/api_client.dart';
import 'app_review/rating_service.dart';
import 'auth/auth_service.dart';
import 'auth/device_service.dart';
import 'auth/device_token_store.dart';
import 'config/app_config.dart';
import 'config/remote_config_service.dart';
import 'notifications/notification_service.dart';
import 'storage/secure_storage.dart';

import 'utils/share_service.dart';

class AppServices {
  AppServices._({
    required this.apiClient,
    required this.deviceService,
    required this.authService,
    required this.postRepository,
    required this.notificationRepository,
    required this.notificationService,
    required this.analyticsService,
    required this.ratingService,
    required this.sessionTracker,
    required this.shareService,
    required this.forceUpdateService,
    required this.remoteConfig,
  });

  final ApiClient apiClient;
  final DeviceService deviceService;
  final AuthService authService;
  final PostRepository postRepository;
  final NotificationRepository notificationRepository;
  final NotificationService notificationService;
  final AnalyticsService analyticsService;
  final RatingService ratingService;
  final SessionTracker sessionTracker;
  final ShareService shareService;
  final ForceUpdateService forceUpdateService;
  final RemoteConfigService remoteConfig;
  static Future<AppServices> create({
    void Function(LogoutReason)? onLogout,
    void Function()? onMaintenance,
  }) async {
    AppConfig.validate();

    final secureStorage = SecureStorage();
    final tokenStore = SecureDeviceTokenStore(secureStorage);

    late final ApiClient apiClient;
    late final AuthService authService;

    apiClient = ApiClient(
      deviceTokenReader: tokenStore.read,
      accessTokenReader: secureStorage.readAccessToken,
      tokenRefresher: () => authService.refreshAccessToken(),
      onMaintenance: onMaintenance,
    );

    authService = AuthService(
      storage: secureStorage,
      apiClient: apiClient,
      onLogout: onLogout,
    );

    final deviceService = DeviceService(
      apiClient: apiClient,
      tokenStore: tokenStore,
    );

    final notificationService = NotificationService(
      deviceService: deviceService,
    );

    final analyticsService = AnalyticsService(apiClient: apiClient);
    final shareService = ShareService(analyticsService: analyticsService);
    final ratingService = RatingService();
    final sessionTracker = await SessionTracker.create();
    await ratingService.incrementSession();

    final remoteConfig = RemoteConfigService();
    // Remote Config is fetched in parallel — failures are handled gracefully
    await remoteConfig.init();

    if (AppRuntime.useRemoteApi) {
      await deviceService.getOrRegisterDeviceToken();
      await authService.init();
      await notificationService.init();
      await analyticsService.setUserType(authService.isLoggedIn);
    }

    return AppServices._(
      apiClient: apiClient,
      deviceService: deviceService,
      authService: authService,
      postRepository: PostRepository(apiClient: apiClient),
      notificationRepository: NotificationRepository(apiClient: apiClient),
      notificationService: notificationService,
      analyticsService: analyticsService,
      ratingService: ratingService,
      sessionTracker: sessionTracker,
      shareService: shareService,
      forceUpdateService: ForceUpdateService(apiClient: apiClient),
      remoteConfig: remoteConfig,
    );
  }
}

abstract final class AppRuntime {
  static const useRemoteApi = bool.fromEnvironment(
    'USE_REMOTE_API',
    defaultValue: false,
  );
}
