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

    final result = await repository.fetchFeed();

    expect(result.posts, hasLength(1));
    expect(result.posts.single.id, 'post-1');
    expect(result.posts.single.category.name, 'İş Hayatı');
    expect(result.posts.single.myVote, VoteType.hakli);
    expect(result.posts.single.voteCountHakli, 12);
    expect(result.posts.single.content, isEmpty);
    expect(result.hasMore, isFalse);
    expect(result.rankingLabel, isNull);
  });

  test('PostRepository parses rankingLabel from feed envelope', () async {
    final repository = PostRepository(
      apiClient: ApiClient(
        dio: _mockDio({
          'posts': [
            {
              'id': 'post-env',
              'title': 'Envelope label test',
              'imageUrl': null,
              'category': {'id': 1, 'name': 'X', 'emoji': 'x'},
              'voteCountHakli': 5,
              'voteCountHaksiz': 2,
              'commentCount': 0,
              'myVote': null,
              'trendScore': 7,
              'createdAt': '2026-05-20T08:00:00Z',
              'isOwner': false,
            }
          ],
          'pagination': {'page': 1, 'limit': 20, 'total': 1, 'hasNext': true},
          'rankingLabel': 'category_trending',
        }),
      ),
    );

    final result = await repository.fetchFeed(categoryId: 1);

    expect(result.rankingLabel, 'category_trending',
        reason: 'envelope-level rankingLabel must be surfaced on FeedResponse');
    expect(result.hasMore, isTrue,
        reason:
            'hasMore must reflect pagination.hasNext from the API response');
    expect(result.posts, hasLength(1));
  });

  test('PostRepository maps product trending sort to backend hot sort',
      () async {
    late RequestOptions request;
    final repository = PostRepository(
      apiClient: ApiClient(
        dio: _mockDio(
          {
            'posts': <Object?>[],
            'pagination': {
              'page': 1,
              'limit': 20,
              'total': 0,
              'hasNext': false,
            },
          },
          onRequest: (options) => request = options,
        ),
      ),
    );

    await repository.fetchFeed(sort: 'trending');

    expect(request.queryParameters['sort'], 'hot');
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

  test('PostRepository parses ranking_reason and ranking_label from post JSON',
      () async {
    final postJson = {
      'id': 'post-3',
      'title': 'Test post',
      'content': 'İçerik',
      'imageUrl': null,
      'category': {'id': 3, 'name': 'Aile', 'emoji': '👨‍👩‍👧'},
      'voteCountHakli': 5,
      'voteCountHaksiz': 2,
      'commentCount': 1,
      'myVote': null,
      'trendScore': 3.1,
      'createdAt': '2026-05-20T08:00:00Z',
      'isOwner': false,
      'ranking_reason': 'rising',
      'ranking_label': 'trending',
    };

    final repository = PostRepository(
      apiClient: ApiClient(
        dio: _mockDio({
          'posts': [postJson],
          'pagination': {'page': 1, 'limit': 20, 'total': 1, 'hasNext': false},
        }),
      ),
    );

    final result = await repository.fetchFeed();

    expect(result.posts.single.rankingReason, 'rising',
        reason:
            'ranking_reason (snake_case) from PostDto must be parsed into Post.rankingReason');
    expect(result.posts.single.rankingLabel, 'trending',
        reason:
            'ranking_label (snake_case) from PostDto must be parsed into Post.rankingLabel');
  });

  test('DiscoverFeedItem rankingReason accepts all valid server values',
      () async {
    const validReasons = ['rising', 'controversial', 'fresh', 'trending'];

    for (final reason in validReasons) {
      final postJson = {
        'id': 'post-r',
        'title': 'T',
        'content': 'C',
        'imageUrl': null,
        'category': {'id': 1, 'name': 'X', 'emoji': 'x'},
        'voteCountHakli': 1,
        'voteCountHaksiz': 0,
        'commentCount': 0,
        'myVote': null,
        'trendScore': 1.0,
        'createdAt': '2026-05-20T08:00:00Z',
        'isOwner': false,
      };

      final repository = PostRepository(
        apiClient: ApiClient(
          dio: _mockDio({
            'items': [
              {
                'post': postJson,
                'rankingReason': reason,
                'impressionToken': 'tok',
                'seenBefore': false,
              }
            ],
            'nextCursor': null,
          }),
        ),
      );

      final feed = await repository.fetchDiscoverFeed();
      expect(feed.items.single.rankingReason, reason,
          reason: 'rankingReason "$reason" must round-trip through the parser');
    }
  });

  test('DiscoverFeedItem rankingReason falls back to trending when absent',
      () async {
    final postJson = {
      'id': 'post-fb',
      'title': 'T',
      'content': 'C',
      'imageUrl': null,
      'category': {'id': 1, 'name': 'X', 'emoji': 'x'},
      'voteCountHakli': 1,
      'voteCountHaksiz': 0,
      'commentCount': 0,
      'myVote': null,
      'trendScore': 1.0,
      'createdAt': '2026-05-20T08:00:00Z',
      'isOwner': false,
    };

    final repository = PostRepository(
      apiClient: ApiClient(
        dio: _mockDio({
          'items': [
            {
              'post': postJson,
              // rankingReason intentionally absent
              'impressionToken': 'tok',
              'seenBefore': false,
            }
          ],
          'nextCursor': null,
        }),
      ),
    );

    final feed = await repository.fetchDiscoverFeed();
    expect(feed.items.single.rankingReason, 'trending',
        reason: 'missing rankingReason must default to "trending"');
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
