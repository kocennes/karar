import '../../../core/api/api_client.dart';
import 'notification_item.dart';

class NotificationsPage {
  const NotificationsPage({
    required this.items,
    required this.unreadCount,
  });

  final List<NotificationItem> items;
  final int unreadCount;
}

class NotificationRepository {
  const NotificationRepository({required ApiClient apiClient})
    : _apiClient = apiClient;

  final ApiClient _apiClient;

  Future<NotificationsPage> fetchNotifications({
    int page = 1,
    int limit = 30,
  }) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      '/api/v1/notifications',
      query: {'page': '$page', 'limit': '$limit'},
    );
    final items = json['notifications'] as List<Object?>? ?? const [];
    return NotificationsPage(
      items: items
          .cast<Map<String, Object?>>()
          .map(_fromJson)
          .toList(growable: false),
      unreadCount: json['unreadCount'] as int? ?? 0,
    );
  }

  Future<void> markAllRead() async {
    await _apiClient.putJson<void>('/api/v1/notifications/read-all');
  }

  NotificationItem _fromJson(Map<String, Object?> json) {
    return NotificationItem(
      id: json['id'] as String,
      type: json['type'] as String,
      title: json['title'] as String,
      body: json['body'] as String,
      postId: json['postId'] as String?,
      isRead: json['isRead'] as bool? ?? false,
      createdAt: DateTime.parse(json['createdAt'] as String),
    );
  }
}
