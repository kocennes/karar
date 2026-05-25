import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/api/api_client.dart';
import 'package:karar/features/feed/post_repository.dart';
import 'package:karar/shared/models/post.dart';

Dio _mockDio(
  Map<String, dynamic> responseBody, {
  int statusCode = 200,
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
            statusCode: statusCode,
            data: statusCode == 204 ? null : responseBody,
          ),
        );
      },
    ),
  );
  return dio;
}

void main() {
  test('PostRepository parses feed response from API contract', () async {
    final feedPostJson = {
      'id': 'post-1',
      'title': 'Arkadaşım benden borç istedi, vermedim',
      'imageUrl': null,
      'category': {'id': 1, 'name': 'İş Hayatı', 'emoji': 'İş'},
      'voteCountHakli': 12,
      'voteCountHaksiz': 3,
      'commentCount': 2,
      'myVote': 'hakli',
      'trendScore': 19,
      'createdAt': '2026-05-15T10:00:00Z',
      'isOwner': false,
    };
    expect(feedPostJson, isNot(contains('content')));

    final repository = PostRepository(
      apiClient: ApiClient(
        dio: _mockDio({
          'posts': [feedPostJson],
          'pagination': {'page': 1, 'limit': 20, 'total': 1, 'hasNext': false},
        }),
      ),
    );

    final posts = await repository.fetchFeed();

    expect(posts, hasLength(1));
    expect(posts.single.id, 'post-1');
    expect(posts.single.category.name, 'İş Hayatı');
    expect(posts.single.myVote, VoteType.hakli);
    expect(posts.single.voteCountHakli, 12);
    expect(posts.single.content, isEmpty);
  });

  test('PostRepository parses categories envelope from API contract', () async {
    final repository = PostRepository(
      apiClient: ApiClient(
        dio: _mockDio({
          'categories': [
            {'id': 1, 'name': 'Is Hayati', 'slug': 'is-hayati', 'emoji': '💼'},
            {'id': 2, 'name': 'Iliskiler', 'slug': 'iliskiler', 'emoji': '❤️'},
          ],
        }),
      ),
    );

    final categories = await repository.fetchCategories();

    expect(categories, hasLength(2));
    expect(categories.first.id, 1);
    expect(categories.first.name, 'Is Hayati');
    expect(categories.first.icon, '💼');
  });

  test('PostRepository parses discover feed response from API contract',
      () async {
    final discoverPostJson = {
      'id': 'post-2',
      'title': 'Komsum gece matkap kullandi, tepki verdim',
      'content': 'Saat 01.00 gibi basladi ve uyarmak zorunda kaldim.',
      'imageUrl': null,
      'category': {'id': 2, 'name': 'Komsuluk', 'emoji': 'ev'},
      'voteCountHakli': 30,
      'voteCountHaksiz': 11,
      'commentCount': 7,
      'myVote': null,
      'trendScore': 42.5,
      'createdAt': '2026-05-15T10:00:00Z',
      'isOwner': false,
      'isAnonymous': true,
    };
    RequestOptions? request;
    final repository = PostRepository(
      apiClient: ApiClient(
        dio: _mockDio(
          {
            'items': [
              {
                'post': discoverPostJson,
                'rankingReason': 'controversial',
                'impressionToken': 'token-1',
                'seenBefore': false,
              }
            ],
            'nextCursor': 'cursor-2',
          },
          onRequest: (options) => request = options,
        ),
      ),
    );

    final feed = await repository.fetchDiscoverFeed(
      cursor: 'cursor-1',
      limit: 5,
    );

    expect(request?.path, '/api/v1/posts/discover/feed');
    expect(request?.queryParameters['cursor'], 'cursor-1');
    expect(request?.queryParameters['limit'], '5');
    expect(feed.items, hasLength(1));
    expect(feed.items.single.post.id, 'post-2');
    expect(feed.items.single.rankingReason, 'controversial');
    expect(feed.items.single.impressionToken, 'token-1');
    expect(feed.items.single.seenBefore, isFalse);
    expect(feed.nextCursor, 'cursor-2');
  });

  test('PostRepository sends discover event payload to API contract', () async {
    RequestOptions? request;
    final repository = PostRepository(
      apiClient: ApiClient(
        dio: _mockDio(
          {},
          statusCode: 204,
          onRequest: (options) => request = options,
        ),
      ),
    );

    await repository.sendDiscoverEvent(
      postId: 'post-2',
      eventType: 'dwell',
      dwellSeconds: 7,
      impressionToken: 'token-1',
    );

    expect(request?.path, '/api/v1/posts/discover/events');
    expect(request?.method, 'POST');
    expect(request?.data, {
      'postId': 'post-2',
      'eventType': 'dwell',
      'dwellSeconds': 7,
      'impressionToken': 'token-1',
    });
  });
}
