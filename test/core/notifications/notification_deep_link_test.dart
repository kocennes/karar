import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/notifications/notification_deep_link.dart';

void main() {
  group('NotificationDeepLink', () {
    test('normalizes documented karar deeplink payloads', () {
      final destination = NotificationDeepLink.fromPayload({
        'deeplink': 'karar://posts/post-1?commentId=comment-1',
        'type': 'comment_on_post',
      });

      expect(
        destination,
        '/posts/post-1?commentId=comment-1&source=notification',
      );
    });

    test('supports existing deepLink casing', () {
      final destination = NotificationDeepLink.fromPayload({
        'deepLink': '/posts/post-2?ref=abc',
      });

      expect(destination, '/posts/post-2?ref=abc&source=notification');
    });

    test('builds legacy post destinations with comment anchors', () {
      final destination = NotificationDeepLink.fromPayload({
        'postId': 'post 3',
        'commentId': 'comment-3',
      });

      expect(
        destination,
        '/posts/post%203?source=notification&commentId=comment-3',
      );
    });

    test('falls back to notification center for unknown payloads', () {
      expect(NotificationDeepLink.fromPayload({}), '/notifications');
      expect(
        NotificationDeepLink.fromPayload({'deepLink': 'https://example.com'}),
        '/notifications',
      );
    });
  });
}
