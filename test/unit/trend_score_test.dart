import 'package:flutter_test/flutter_test.dart';
import 'package:karar/shared/models/post.dart';

void main() {
  group('TrendScore Calculation', () {
    final now = DateTime.now();
    test('Score increases with votes and comments', () {
      const category = Category(id: 1, name: 'Test', icon: 'T');
      final post1 = Post(
        id: '1',
        category: category,
        title: 'Title',
        content: 'Content',
        createdAgo: '1h ago',
        voteCountHakli: 10,
        voteCountHaksiz: 5,
        commentCount: 2,
        comments: [],
        createdAt: now,
      );
      
      final post2 = Post(
        id: '2',
        category: category,
        title: 'Title',
        content: 'Content',
        createdAgo: '1h ago',
        voteCountHakli: 20,
        voteCountHaksiz: 10,
        commentCount: 5,
        comments: [],
        createdAt: now,
      );

      expect(post2.trendScore, greaterThan(post1.trendScore));
    });

    test('Score is non-negative for zero engagement', () {
      const category = Category(id: 1, name: 'Test', icon: 'T');
      final post = Post(
        id: '1',
        category: category,
        title: 'Title',
        content: 'Content',
        createdAgo: '1h ago',
        voteCountHakli: 0,
        voteCountHaksiz: 0,
        commentCount: 0,
        comments: [],
        createdAt: now,
      );
      
      expect(post.trendScore, greaterThanOrEqualTo(0));
    });
  });
}
