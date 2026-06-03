import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/data/sample_posts.dart';
import '../../shared/models/post.dart';
import 'data/notification_item.dart';
import 'data/notification_repository.dart';
import 'sse_notification_provider.dart';

class NotificationsState {
  const NotificationsState({
    this.items = const [],
    this.unreadCount = 0,
    this.isLoading = false,
    this.error,
  });

  final List<NotificationItem> items;
  final int unreadCount;
  final bool isLoading;
  final String? error;

  bool get hasReadItems => items.any((i) => i.isRead);

  NotificationsState copyWith({
    List<NotificationItem>? items,
    int? unreadCount,
    bool? isLoading,
    String? error,
    bool clearError = false,
  }) =>
      NotificationsState(
        items: items ?? this.items,
        unreadCount: unreadCount ?? this.unreadCount,
        isLoading: isLoading ?? this.isLoading,
        error: clearError ? null : (error ?? this.error),
      );
}

class NotificationsNotifier extends Notifier<NotificationsState> {
  @override
  NotificationsState build() {
    if (AppRuntime.useRemoteApi) {
      ref.listen(sseNotificationProvider, (_, event) {
        event.whenData(_onSseEvent);
      });
    }
    Future.microtask(load);
    return const NotificationsState(isLoading: true);
  }

  void _onSseEvent(Map<String, dynamic> data) {
    final type = data['type'] as String?;
    final unreadCount = data['unreadCount'] as int?;
    if (unreadCount == null || type == null) return;
    if (type == 'notification.created' ||
        type == 'notification.read' ||
        type == 'connected') {
      state = state.copyWith(unreadCount: unreadCount);
    }
  }

  NotificationRepository get _repo => ref.read(notificationRepositoryProvider);

  Future<void> load() async {
    if (!AppRuntime.useRemoteApi) {
      await Future<void>.delayed(const Duration(milliseconds: 500));
      state = state.copyWith(
        items: [
          NotificationItem(
            id: '1',
            type: 'verdict_milestone',
            title: 'Topluluk karar verdi!',
            body: 'Paylaşımın 10 oya ulaştı. %78 Haklı buluyor.',
            isRead: false,
            createdAt: DateTime.now().subtract(const Duration(minutes: 15)),
            postId: '1',
          ),
          NotificationItem(
            id: '2',
            type: 'comment_on_post',
            title: 'Yeni yorum',
            body: 'Paylaşımına yeni bir yorum geldi.',
            isRead: true,
            createdAt: DateTime.now().subtract(const Duration(hours: 2)),
            postId: '1',
          ),
        ],
        unreadCount: 1,
        isLoading: false,
      );
      return;
    }

    state = state.copyWith(isLoading: true, clearError: true);
    try {
      final page = await ref.read(performanceServiceProvider).trace(
            'notification_center_load',
            () => _repo.fetchNotifications(),
          );
      state = state.copyWith(
        items: page.items,
        unreadCount: page.unreadCount,
        isLoading: false,
      );
    } catch (_) {
      state = state.copyWith(
        isLoading: false,
        error: 'Bildirimler yüklenemedi.',
      );
    }
  }

  Future<void> syncUnreadCount() async {
    if (!AppRuntime.useRemoteApi) return;

    try {
      final unreadCount = await _repo.fetchUnreadCount();
      state = state.copyWith(unreadCount: unreadCount);
    } catch (_) {}
  }

  Future<void> markAllRead() async {
    try {
      if (AppRuntime.useRemoteApi) await _repo.markAllRead();
      state = state.copyWith(
        items: [for (final item in state.items) item.copyWith(isRead: true)],
        unreadCount: 0,
      );
      await syncUnreadCount();
    } catch (_) {}
  }

  Future<void> markRead(String id) async {
    final item = state.items.where((i) => i.id == id).firstOrNull;
    if (item == null || item.isRead) return;

    // Optimistic update
    state = state.copyWith(
      items: [
        for (final i in state.items)
          if (i.id == id) i.copyWith(isRead: true) else i,
      ],
      unreadCount: (state.unreadCount - 1).clamp(0, state.unreadCount),
    );

    try {
      if (AppRuntime.useRemoteApi) await _repo.markRead(id);
    } catch (_) {
      // Rollback
      state = state.copyWith(
        items: [
          for (final i in state.items)
            if (i.id == id) i.copyWith(isRead: false) else i,
        ],
        unreadCount: state.unreadCount + 1,
      );
    }
  }

  Future<void> markOpened(String id) async {
    if (!AppRuntime.useRemoteApi) return;

    try {
      await _repo.markOpened(id);
    } catch (_) {}
  }

  Future<void> dismiss(String id) async {
    final item = state.items.where((i) => i.id == id).firstOrNull;
    if (item == null) return;

    // Optimistic update
    final wasUnread = !item.isRead;
    state = state.copyWith(
      items: [
        for (final i in state.items)
          if (i.id != id) i
      ],
      unreadCount: wasUnread
          ? (state.unreadCount - 1).clamp(0, state.unreadCount)
          : state.unreadCount,
    );

    try {
      if (AppRuntime.useRemoteApi) await _repo.dismiss(id);
    } catch (_) {
      // Rollback — reload from server
      load();
    }
  }

  Future<void> clearRead() async {
    final clearedCount = state.items.where((i) => i.isRead).length;
    if (clearedCount == 0) return;

    // Optimistic update
    state = state.copyWith(
      items: [
        for (final i in state.items)
          if (!i.isRead) i
      ],
    );

    try {
      if (AppRuntime.useRemoteApi) await _repo.clearRead();
    } catch (_) {
      load();
    }
  }

  Future<bool> mute(String duration) async {
    try {
      if (AppRuntime.useRemoteApi) await _repo.mute(duration);
      return true;
    } catch (_) {
      return false;
    }
  }
}

final notificationsProvider =
    NotifierProvider<NotificationsNotifier, NotificationsState>(
  NotificationsNotifier.new,
);

final digestPostsProvider = FutureProvider.autoDispose<List<Post>>((ref) async {
  if (!AppRuntime.useRemoteApi) {
    await Future<void>.delayed(const Duration(milliseconds: 300));
    final sorted = [...samplePosts]
      ..sort((a, b) => b.totalVotes.compareTo(a.totalVotes));
    return sorted.take(3).toList();
  }
  try {
    return (await ref.read(postRepositoryProvider).fetchFeed(
              sort: 'trending',
              limit: 3,
            ))
        .posts;
  } catch (_) {
    return [];
  }
});
