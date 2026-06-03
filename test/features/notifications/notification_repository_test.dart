import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/api/api_client.dart';
import 'package:karar/features/notifications/data/notification_repository.dart';

Dio _mockDio(
  Map<String, dynamic> responseBody, {
  void Function(RequestOptions options)? onRequest,
}) {
  final dio = Dio(BaseOptions(baseUrl: 'http://localhost'));
  dio.interceptors.add(
    InterceptorsWrapper(
      onRequest: (options, handler) {
        onRequest?.call(options);
        handler.resolve(
          Response(
            requestOptions: options,
            statusCode: options.method == 'GET' ? 200 : 204,
            data: responseBody,
          ),
        );
      },
    ),
  );
  return dio;
}

void main() {
  test('NotificationRepository parses unread count from API contract',
      () async {
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

  test('NotificationRepository fetches unread count from badge sync endpoint',
      () async {
    final repository = NotificationRepository(
      apiClient: ApiClient(
        dio: _mockDio({'unreadCount': 3}),
      ),
    );

    final unreadCount = await repository.fetchUnreadCount();

    expect(unreadCount, 3);
  });

  test('NotificationRepository records opened lifecycle event', () async {
    RequestOptions? request;
    final repository = NotificationRepository(
      apiClient: ApiClient(
        dio: _mockDio({}, onRequest: (options) => request = options),
      ),
    );

    await repository.markOpened('notification-1');

    expect(request?.method, 'POST');
    expect(request?.path, '/api/v1/notifications/notification-1/opened');
  });

  test('NotificationRepository mutes notifications with backend duration',
      () async {
    RequestOptions? request;
    final repository = NotificationRepository(
      apiClient: ApiClient(
        dio: _mockDio({}, onRequest: (options) => request = options),
      ),
    );

    await repository.mute('7d');

    expect(request?.method, 'POST');
    expect(request?.path, '/api/v1/notifications/mute');
    expect(request?.data, {'duration': '7d'});
  });
}
