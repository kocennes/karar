import 'package:flutter_test/flutter_test.dart';
import 'package:karar/features/feed/feed_cache.dart';
import 'package:karar/shared/data/sample_posts.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues({});
  });

  test('FeedCache stores and restores posts by sort and category', () async {
    const cache = FeedCache();
    final post = samplePosts.first;

    await cache.write(sort: 'new', categoryId: post.category.id, posts: [post]);

    final restored = await cache.read(
      sort: 'new',
      categoryId: post.category.id,
    );

    expect(restored, hasLength(1));
    expect(restored.single.id, post.id);
    expect(restored.single.title, post.title);
    expect(restored.single.category.id, post.category.id);
    expect(restored.single.voteCountHakli, post.voteCountHakli);
  });

  test('FeedCache keeps different feed variants separate', () async {
    const cache = FeedCache();

    await cache.write(
      sort: 'trending',
      categoryId: null,
      posts: [samplePosts.first],
    );
    await cache.write(
      sort: 'new',
      categoryId: samplePosts.last.category.id,
      posts: [samplePosts.last],
    );

    final trending = await cache.read(sort: 'trending', categoryId: null);
    final filtered = await cache.read(
      sort: 'new',
      categoryId: samplePosts.last.category.id,
    );

    expect(trending.single.id, samplePosts.first.id);
    expect(filtered.single.id, samplePosts.last.id);
  });
}
