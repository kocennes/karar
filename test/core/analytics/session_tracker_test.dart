import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/analytics/session_tracker.dart';

void main() {
  group('SessionStats defaults', () {
    test('postsCreated defaults to 0', () {
      const stats = SessionStats(
        durationSeconds: 10,
        postsViewed: 0,
        votesCast: 0,
        commentsPosted: 0,
      );
      expect(stats.postsCreated, 0);
    });

    test('maxFeedPosition defaults to -1', () {
      const stats = SessionStats(
        durationSeconds: 10,
        postsViewed: 0,
        votesCast: 0,
        commentsPosted: 0,
      );
      expect(stats.maxFeedPosition, -1);
    });

    test('maxDiscoverPosition defaults to -1', () {
      const stats = SessionStats(
        durationSeconds: 10,
        postsViewed: 0,
        votesCast: 0,
        commentsPosted: 0,
      );
      expect(stats.maxDiscoverPosition, -1);
    });
  });

  group('SessionTracker counters', () {
    late SessionTracker tracker;

    setUp(() {
      tracker = SessionTracker.testInstance();
    });

    test('incrementPostCreated increments postsCreated', () {
      tracker.incrementPostCreated();
      tracker.incrementPostCreated();
      final stats = tracker.snapshot();
      expect(stats.postsCreated, 2);
    });

    test('updateMaxFeedPosition tracks highest position', () {
      tracker.updateMaxFeedPosition(3);
      tracker.updateMaxFeedPosition(7);
      tracker.updateMaxFeedPosition(2);
      final stats = tracker.snapshot();
      expect(stats.maxFeedPosition, 7);
    });

    test('updateMaxDiscoverPosition tracks highest position', () {
      tracker.updateMaxDiscoverPosition(0);
      tracker.updateMaxDiscoverPosition(5);
      tracker.updateMaxDiscoverPosition(3);
      final stats = tracker.snapshot();
      expect(stats.maxDiscoverPosition, 5);
    });

    test('snapshot does not reset counters', () {
      tracker.incrementPostCreated();
      tracker.updateMaxFeedPosition(4);
      tracker.snapshot();
      final stats2 = tracker.snapshot();
      expect(stats2.postsCreated, 1);
      expect(stats2.maxFeedPosition, 4);
    });

    test('flush resets new counters', () {
      tracker.incrementPostCreated();
      tracker.updateMaxFeedPosition(4);
      tracker.updateMaxDiscoverPosition(2);
      tracker.flush();
      final stats = tracker.snapshot();
      expect(stats.postsCreated, 0);
      expect(stats.maxFeedPosition, -1);
      expect(stats.maxDiscoverPosition, -1);
    });

    test('flush returns accumulated stats before reset', () {
      tracker.incrementPostCreated();
      tracker.updateMaxFeedPosition(6);
      tracker.updateMaxDiscoverPosition(9);
      final stats = tracker.flush();
      expect(stats.postsCreated, 1);
      expect(stats.maxFeedPosition, 6);
      expect(stats.maxDiscoverPosition, 9);
    });

    test('existing counters still work after new fields added', () {
      tracker.incrementPostViewed();
      tracker.incrementPostViewed();
      tracker.incrementVoteCast();
      tracker.incrementCommentPosted();
      final stats = tracker.flush();
      expect(stats.postsViewed, 2);
      expect(stats.votesCast, 1);
      expect(stats.commentsPosted, 1);
    });
  });
}
