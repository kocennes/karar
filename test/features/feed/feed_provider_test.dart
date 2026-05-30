import 'package:flutter_test/flutter_test.dart';
import 'package:karar/features/feed/feed_provider.dart';

void main() {
  group('FeedState', () {
    test('initial rankingLabel is null', () {
      expect(FeedState().rankingLabel, isNull);
    });

    test('copyWith preserves rankingLabel when not provided', () {
      final state = FeedState(rankingLabel: 'trending');
      final updated = state.copyWith(isLoading: true);
      expect(updated.rankingLabel, 'trending');
    });

    test('copyWith updates rankingLabel', () {
      final state = FeedState(rankingLabel: 'trending');
      final updated = state.copyWith(rankingLabel: 'category_new');
      expect(updated.rankingLabel, 'category_new');
    });

    test('copyWith with null rankingLabel keeps existing value', () {
      final state = FeedState(rankingLabel: 'new');
      // null means "no override" — existing value is preserved
      final updated = state.copyWith(rankingLabel: null);
      expect(updated.rankingLabel, 'new');
    });

    test('all valid rankingLabel values round-trip through FeedState', () {
      const labels = ['trending', 'new', 'category_trending', 'category_new'];
      for (final label in labels) {
        final state = FeedState(rankingLabel: label);
        expect(state.rankingLabel, label,
            reason: 'rankingLabel "$label" must be stored on FeedState');
        expect(state.copyWith().rankingLabel, label,
            reason: 'copyWith must preserve "$label"');
      }
    });

    test('feedRankingLabelFor maps sort and category to API labels', () {
      expect(feedRankingLabelFor(sort: 'trending'), 'trending');
      expect(feedRankingLabelFor(sort: 'new'), 'new');
      expect(
        feedRankingLabelFor(sort: 'trending', categoryId: 3),
        'category_trending',
      );
      expect(feedRankingLabelFor(sort: 'new', categoryId: 3), 'category_new');
    });
  });
}
