import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/api/api_client.dart';
import 'package:karar/features/feed/post_repository.dart';
import 'package:karar/shared/models/post.dart';

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
}
