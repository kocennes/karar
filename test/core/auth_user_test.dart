import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/auth/auth_service.dart';

void main() {
  test('AuthUser parses profile counters and joined date', () {
    final user = AuthUser.fromJson({
      'id': 'user-1',
      'username': 'kararci',
      'email': 'kararci@example.com',
      'karma': 42,
      'authProvider': 'password',
      'postCount': 7,
      'commentCount': 13,
      'joinedAt': '2026-04-01T00:00:00Z',
    });

    expect(user.postCount, 7);
    expect(user.commentCount, 13);
    expect(user.joinedAt, DateTime.parse('2026-04-01T00:00:00Z'));
  });

  test('AuthUser accepts API auth session shape', () {
    final user = AuthUser.fromJson({
      'userId': 'user-1',
      'username': 'kararci',
      'email': 'kararci@example.com',
      'karma': 42,
      'authProvider': 'password',
      'accessToken': 'access-token',
      'refreshToken': 'refresh-token',
      'accessTokenExpiresAt': '2026-05-15T11:30:00Z',
    });

    expect(user.id, 'user-1');
    expect(user.username, 'kararci');
  });
}
