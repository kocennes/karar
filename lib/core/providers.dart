import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'auth/auth_service.dart';
import 'analytics/analytics_service.dart';
import 'analytics/performance_service.dart';
import 'analytics/session_tracker.dart';
import 'app_review/rating_service.dart';
import 'notifications/notification_service.dart';
import '../features/feed/post_repository.dart';
import '../features/notifications/data/notification_repository.dart';

import 'api/api_client.dart';
import 'config/remote_config_service.dart';
import 'storage/post_draft_service.dart';
import 'update/force_update_service.dart';
import 'utils/share_service.dart';

final authServiceProvider = Provider<AuthService>(
  (ref) => throw UnimplementedError('Override in ProviderScope'),
);

final apiClientProvider = Provider<ApiClient>(
  (ref) => throw UnimplementedError('Override in ProviderScope'),
);

final shareServiceProvider = Provider<ShareService>(
  (ref) => throw UnimplementedError('Override in ProviderScope'),
);

final postRepositoryProvider = Provider<PostRepository>(
  (ref) => throw UnimplementedError('Override in ProviderScope'),
);

final notificationRepositoryProvider = Provider<NotificationRepository>(
  (ref) => throw UnimplementedError('Override in ProviderScope'),
);

final analyticsServiceProvider = Provider<AnalyticsService>(
  (ref) => throw UnimplementedError('Override in ProviderScope'),
);

final performanceServiceProvider = Provider<PerformanceService>(
  (_) => PerformanceService(),
);

final ratingServiceProvider = Provider<RatingService>(
  (ref) => throw UnimplementedError('Override in ProviderScope'),
);

final notificationServiceProvider = Provider<NotificationService>(
  (ref) => throw UnimplementedError('Override in ProviderScope'),
);

final sessionTrackerProvider = Provider<SessionTracker>(
  (ref) => throw UnimplementedError('Override in ProviderScope'),
);

final forceUpdateServiceProvider = Provider<ForceUpdateService>(
  (ref) => throw UnimplementedError('Override in ProviderScope'),
);

// Reactive auth state — updated on login/logout
final currentUserProvider = StateProvider<AuthUser?>((ref) => null);

// Logout reason — used to show messages
final logoutReasonProvider = StateProvider<LogoutReason?>((ref) => null);

// Pending e-posta değişikliği — set after changeEmail request, cleared after confirm
final pendingEmailChangeProvider = StateProvider<String?>((ref) => null);

// Maintenance mode — set to true when backend returns 503
final maintenanceProvider = StateProvider<bool>((ref) => false);

// Post taslak servisi — SharedPreferences üzerinde çalışır, singleton
final postDraftServiceProvider = Provider<PostDraftService>(
  (_) => PostDraftService(),
);

// Firebase Remote Config — uygulama genelinde feature flag yönetimi
final remoteConfigProvider = Provider<RemoteConfigService>(
  (_) => RemoteConfigService(),
);
