import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/api/api_client.dart';
import 'package:karar/features/notifications/data/notification_repository.dart';

Dio _mockDio(Map<String, dynamic> responseBody) {
  final dio = Dio(BaseOptions(baseUrl: 'http://localhost'));
  dio.interceptors.add(
    InterceptorsWrapper(
      onRequest: (options, handler) => handler.resolve(
        Response(
          requestOptions: options,
          statusCode: 200,
          data: responseBody,
        ),
      ),
    ),
  );
  return dio;
}

void main() {
  test('NotificationRepository parses unread count from API contract', () async {
    final repository = NotificationRepository(
      apiClient: ApiClient(
        dio: _mockDio({
          'notifications': [
            {
              'id': 'notification-1',
              'type': 'comment_on_post',
              'title': 'Yeni yorum',
              'body': 'Postuna yeni yorum geldi.',
              'postId': 'post-1',
              'isRead': false,
              'createdAt': '2026-05-15T10:00:00Z',
            }
          ],
          'pagination': {'page': 1, 'limit': 30, 'total': 1, 'hasNext': false},
          'unreadCount': 1,
        }),
      ),
    );

    final page = await repository.fetchNotifications();

    expect(page.items, hasLength(1));
    expect(page.items.single.id, 'notification-1');
    expect(page.items.single.postId, 'post-1');
    expect(page.unreadCount, 1);
  });
}
