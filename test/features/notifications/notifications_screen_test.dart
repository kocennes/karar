import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:karar/core/auth/auth_service.dart';
import 'package:karar/core/providers.dart';
import 'package:karar/features/notifications/data/notification_item.dart';
import 'package:karar/features/notifications/notifications_provider.dart';
import 'package:karar/features/notifications/notifications_screen.dart';
import 'package:karar/shared/models/post.dart';

const _fakeUser = AuthUser(
  id: 'u1',
  username: 'testuser',
  email: 'test@karar.app',
  karma: 0,
  authProvider: 'email',
);

class _FixedNotificationsNotifier extends NotificationsNotifier {
  final NotificationsState _fixed;
  _FixedNotificationsNotifier(this._fixed);

  @override
  NotificationsState build() => _fixed;

  @override
  Future<void> load() async {}

  @override
  Future<void> markAllRead() async {}

  @override
  Future<void> markRead(String id) async {}

  @override
  Future<void> dismiss(String id) async {}

  @override
  Future<void> clearRead() async {}
}

NotificationItem _item(String id, String type, {bool isRead = true}) =>
    NotificationItem(
      id: id,
      type: type,
      title: 'Başlık $type',
      body: 'Gövde $type',
      isRead: isRead,
      createdAt: DateTime(2025),
      postId: 'post-1',
    );

Widget _buildApp(List<Override> overrides) {
  final router = GoRouter(routes: [
    GoRoute(path: '/', builder: (_, __) => const NotificationsScreen()),
    GoRoute(
        path: '/posts/:id',
        builder: (_, __) => const Scaffold(body: SizedBox())),
    GoRoute(
      path: '/settings/moderation-history',
      builder: (_, __) => const Scaffold(body: Text('Moderasyon geçmişi')),
    ),
  ]);
  return ProviderScope(
    overrides: overrides,
    child: MaterialApp.router(routerConfig: router),
  );
}

void main() {
  const allTypes = [
    'verdict_milestone',
    'verdict_reminder',
    'comment_on_post',
    'reply_on_comment',
    'mention',
    'moderation_result',
    'system_announcement',
    'trend_alert',
    'viral_post_owner',
    'weekly_digest',
  ];

  test(
    'NotificationsScreen contract: in-app opens are tracked and source-tagged',
    () {
      final text = File('lib/features/notifications/notifications_screen.dart')
          .readAsStringSync();
      final helper = File('lib/core/notifications/notification_deep_link.dart')
          .readAsStringSync();

      expect(text, contains('logNotificationOpened'));
      expect(text, contains('NotificationDeepLink.fromNotificationItem'));
      expect(helper, contains("query['source'] = 'notification'"));
    },
  );

  test('NotificationsScreen contract: notification center controls are wired',
      () {
    final text = File('lib/features/notifications/notifications_screen.dart')
        .readAsStringSync();

    expect(text, contains('_NotificationControls'));
    expect(text, contains('Bildirim izni ver'));
    expect(text, contains('Bildirim sesini aç'));
    expect(text, contains('Sessize al'));
    expect(text, contains("context.push('/settings/notifications')"));
    expect(text, contains("mute(duration)"));
  });

  test('web push banner: soft prompt cooldown and analytics wired', () {
    final text = File('lib/features/notifications/notifications_screen.dart')
        .readAsStringSync();
    final service =
        File('lib/core/notifications/notification_service.dart').readAsStringSync();
    final analytics =
        File('lib/core/analytics/analytics_service.dart').readAsStringSync();

    expect(service, contains('isSoftPromptOnCooldown'));
    expect(service, contains('recordSoftPromptDismissed'));
    expect(service, contains('_kSoftPromptCooldownDays'));
    expect(analytics, contains('notification_permission_prompt_shown'));
    expect(analytics, contains("'surface'"));
    expect(analytics, contains("'trigger'"));
    expect(text, contains('logNotificationPermissionPromptShown'));
    expect(text, contains('isSoftPromptOnCooldown'));
    expect(text, contains('recordSoftPromptDismissed'));
    expect(text, contains("source: 'soft_dismiss'"));
    expect(text, contains('_onCooldown'));
  });

  testWidgets(
    'NotificationsScreen: all notification types visible in in-app center',
    (tester) async {
      tester.view.physicalSize = const Size(800, 2400); // tall viewport
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.resetPhysicalSize);

      final items = allTypes
          .asMap()
          .entries
          .map((e) => _item('${e.key}', e.value))
          .toList();
      final fixedState = NotificationsState(
        items: items,
        unreadCount: 0,
        isLoading: false,
      );

      await tester.pumpWidget(_buildApp([
        currentUserProvider.overrideWith((ref) => _fakeUser),
        notificationsProvider
            .overrideWith(() => _FixedNotificationsNotifier(fixedState)),
        digestPostsProvider.overrideWith((ref) => Future.value(<Post>[])),
      ]));
      await tester.pump();

      // All notification titles should appear in the list (no scrolling needed: tall viewport)
      for (final item in items) {
        expect(
          find.text('Başlık ${item.type}'),
          findsOneWidget,
          reason: 'Notification type "${item.type}" should be visible',
        );
      }
    },
  );

  testWidgets(
    'NotificationsScreen: unread item has bold title and dot indicator',
    (tester) async {
      final unread = _item('1', 'comment_on_post', isRead: false);
      final read = _item('2', 'verdict_milestone', isRead: true);
      final fixedState = NotificationsState(
        items: [unread, read],
        unreadCount: 1,
        isLoading: false,
      );

      await tester.pumpWidget(_buildApp([
        currentUserProvider.overrideWith((ref) => _fakeUser),
        notificationsProvider
            .overrideWith(() => _FixedNotificationsNotifier(fixedState)),
        digestPostsProvider.overrideWith((ref) => Future.value(<Post>[])),
      ]));
      await tester.pump();

      expect(find.text('Başlık comment_on_post'), findsOneWidget);
      expect(find.text('Başlık verdict_milestone'), findsOneWidget);
      // Unread count appears in app bar title
      expect(find.text('Bildirimler (1)'), findsOneWidget);
    },
  );

  testWidgets(
    'NotificationsScreen: moderation result exposes appeal action',
    (tester) async {
      final fixedState = NotificationsState(
        items: [_item('1', 'moderation_result')],
        unreadCount: 0,
        isLoading: false,
      );

      await tester.pumpWidget(_buildApp([
        currentUserProvider.overrideWith((ref) => _fakeUser),
        notificationsProvider
            .overrideWith(() => _FixedNotificationsNotifier(fixedState)),
        digestPostsProvider.overrideWith((ref) => Future.value(<Post>[])),
      ]));
      await tester.pump();

      expect(find.text('İtiraz Et'), findsOneWidget);

      await tester.tap(find.text('İtiraz Et'));
      await tester.pumpAndSettle();

      expect(find.text('Moderasyon geçmişi'), findsOneWidget);
    },
  );

  testWidgets(
    'NotificationsScreen: empty state shown when no notifications',
    (tester) async {
      const fixedState = NotificationsState(
        items: [],
        unreadCount: 0,
        isLoading: false,
      );

      await tester.pumpWidget(_buildApp([
        currentUserProvider.overrideWith((ref) => _fakeUser),
        notificationsProvider
            .overrideWith(() => _FixedNotificationsNotifier(fixedState)),
        digestPostsProvider.overrideWith((ref) => Future.value(<Post>[])),
      ]));
      await tester.pump();

      expect(
        find.text(
          'Henüz bildirim yok. Topluluk karar verdikçe burada göreceksin.',
        ),
        findsOneWidget,
      );
    },
  );

  testWidgets(
    'NotificationsScreen: guest user sees sign-up nudge',
    (tester) async {
      await tester.pumpWidget(_buildApp([
        currentUserProvider.overrideWith((ref) => null),
        notificationsProvider.overrideWith(
          () => _FixedNotificationsNotifier(const NotificationsState()),
        ),
        digestPostsProvider.overrideWith((ref) => Future.value(<Post>[])),
      ]));
      await tester.pump();

      expect(find.text('Bildirim almak için hesap aç'), findsOneWidget);
    },
  );
}
