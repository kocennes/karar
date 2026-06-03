import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/analytics/judgment_loop_tracker.dart';

void main() {
  group('JudgmentLoopTracker — full loop completes', () {
    test('completes loop when all four stages occur in order', () {
      final tracker = JudgmentLoopTracker();

      tracker.onImpression('post-1', 'feed');
      tracker.onMeaningfulDwell('post-1', 8);
      tracker.onVoted('post-1');
      final result = tracker.onVerdictViewed('post-1');

      expect(result, isNotNull);
      expect(result!.postId, 'post-1');
      expect(result.source, 'feed');
      expect(result.dwellSeconds, 8);
      expect(result.votedBeforeResult, isTrue);
    });

    test('loopDurationSeconds is non-negative', () {
      final tracker = JudgmentLoopTracker();
      tracker.onImpression('post-2', 'discover');
      tracker.onMeaningfulDwell('post-2', 10);
      tracker.onVoted('post-2');
      final result = tracker.onVerdictViewed('post-2');

      expect(result, isNotNull);
      expect(result!.loopDurationSeconds, greaterThanOrEqualTo(0));
    });

    test('clears active loop after completion', () {
      final tracker = JudgmentLoopTracker();
      tracker.onImpression('post-3', 'notification');
      tracker.onMeaningfulDwell('post-3', 6);
      tracker.onVoted('post-3');
      tracker.onVerdictViewed('post-3');

      expect(tracker.hasActiveLoop, isFalse);
    });
  });

  group('JudgmentLoopTracker — incomplete loops return null', () {
    test('returns null when meaningful dwell was skipped (no reading stage)', () {
      final tracker = JudgmentLoopTracker();
      tracker.onImpression('post-4', 'feed');
      // No onMeaningfulDwell → stage stays at impression
      tracker.onVoted('post-4');
      final result = tracker.onVerdictViewed('post-4');

      expect(result, isNull,
          reason: 'loop without meaningful dwell must not complete');
    });

    test('returns null when vote was skipped', () {
      final tracker = JudgmentLoopTracker();
      tracker.onImpression('post-5', 'feed');
      tracker.onMeaningfulDwell('post-5', 7);
      // No onVoted → stage stays at reading
      final result = tracker.onVerdictViewed('post-5');

      expect(result, isNull, reason: 'loop without vote must not complete');
    });

    test('returns null when wrong postId passed to onVerdictViewed', () {
      final tracker = JudgmentLoopTracker();
      tracker.onImpression('post-6', 'feed');
      tracker.onMeaningfulDwell('post-6', 5);
      tracker.onVoted('post-6');
      final result = tracker.onVerdictViewed('wrong-id');

      expect(result, isNull);
    });

    test('returns null when no active loop', () {
      final tracker = JudgmentLoopTracker();
      final result = tracker.onVerdictViewed('post-7');
      expect(result, isNull);
    });
  });

  group('JudgmentLoopTracker — 30-minute timeout abandons loop', () {
    test('loop is abandoned and returns null after 30 minutes', () {
      final fakeNow = _FakeClock(DateTime(2025, 1, 1, 10, 0));
      final tracker = JudgmentLoopTracker(clock: fakeNow.now);

      tracker.onImpression('post-8', 'feed');
      tracker.onMeaningfulDwell('post-8', 12);
      tracker.onVoted('post-8');

      fakeNow.advance(const Duration(minutes: 31));

      final result = tracker.onVerdictViewed('post-8');
      expect(result, isNull,
          reason: 'loop older than 30 minutes must be abandoned');
      expect(tracker.hasActiveLoop, isFalse);
    });

    test('loop within 30 minutes still completes', () {
      final fakeNow = _FakeClock(DateTime(2025, 1, 1, 10, 0));
      final tracker = JudgmentLoopTracker(clock: fakeNow.now);

      tracker.onImpression('post-9', 'feed');
      tracker.onMeaningfulDwell('post-9', 9);
      tracker.onVoted('post-9');

      fakeNow.advance(const Duration(minutes: 29));

      final result = tracker.onVerdictViewed('post-9');
      expect(result, isNotNull,
          reason: 'loop under 30 minutes must still complete');
    });

    test('timed-out loop is cleared; new impression starts fresh', () {
      final fakeNow = _FakeClock(DateTime(2025, 1, 1, 10, 0));
      final tracker = JudgmentLoopTracker(clock: fakeNow.now);

      tracker.onImpression('post-stale', 'feed');
      fakeNow.advance(const Duration(minutes: 31));

      tracker.onImpression('post-10', 'discover');
      expect(tracker.activePostId, 'post-10');
    });
  });

  group('JudgmentLoopTracker — source attribution', () {
    test('notification source is preserved in completed loop', () {
      final tracker = JudgmentLoopTracker();
      tracker.onImpression('post-11', 'notification');
      tracker.onMeaningfulDwell('post-11', 6);
      tracker.onVoted('post-11');
      final result = tracker.onVerdictViewed('post-11');

      expect(result, isNotNull);
      expect(result!.source, 'notification',
          reason:
              'notification source must be preserved to correctly attribute '
              'notification-driven judgment loops in the north-star metric');
    });

    test('discover source is preserved', () {
      final tracker = JudgmentLoopTracker();
      tracker.onImpression('post-12', 'discover');
      tracker.onMeaningfulDwell('post-12', 5);
      tracker.onVoted('post-12');
      final result = tracker.onVerdictViewed('post-12');

      expect(result!.source, 'discover');
    });

    test('share_landing source is preserved', () {
      final tracker = JudgmentLoopTracker();
      tracker.onImpression('post-13', 'share_landing');
      tracker.onMeaningfulDwell('post-13', 5);
      tracker.onVoted('post-13');
      final result = tracker.onVerdictViewed('post-13');

      expect(result!.source, 'share_landing');
    });
  });

  group('JudgmentLoopTracker — new impression replaces active loop', () {
    test('second impression for a different post replaces the first', () {
      final tracker = JudgmentLoopTracker();

      tracker.onImpression('post-old', 'feed');
      tracker.onMeaningfulDwell('post-old', 7);

      tracker.onImpression('post-new', 'discover');
      tracker.onMeaningfulDwell('post-new', 8);
      tracker.onVoted('post-new');
      final result = tracker.onVerdictViewed('post-new');

      expect(result, isNotNull);
      expect(result!.postId, 'post-new');
    });
  });

  // ── Source-file contract tests ─────────────────────────────────────────────

  group('AnalyticsService — judgment loop contract', () {
    late String serviceText;
    setUpAll(() {
      serviceText =
          File('lib/core/analytics/analytics_service.dart').readAsStringSync();
    });

    test('has logCompletedJudgmentLoop method', () {
      expect(serviceText, contains('logCompletedJudgmentLoop'));
    });

    test('has logMeaningfulDwell method', () {
      expect(serviceText, contains('logMeaningfulDwell'));
    });

    test('sets weekly_loops_count user property', () {
      expect(serviceText, contains('weekly_loops_count'));
    });

    test('uses Monday-based weekly reset key', () {
      expect(serviceText, contains('_kLastResetMondayKey'));
    });

    test('posts to loopCompleted backend endpoint', () {
      expect(serviceText, contains('ApiEndpoints.loopCompleted'));
    });
  });

  group('PostDetailScreen — state machine integration', () {
    late String screenText;
    setUpAll(() {
      screenText =
          File('lib/features/post_detail/post_detail_screen.dart').readAsStringSync();
    });

    test('reads judgmentLoopTrackerProvider', () {
      expect(screenText, contains('judgmentLoopTrackerProvider'));
    });

    test('calls onImpression on post open', () {
      expect(screenText, contains('onImpression'));
    });

    test('starts 5-second meaningful dwell timer', () {
      expect(screenText, contains('Duration(seconds: 5)'));
      expect(screenText, contains('onMeaningfulDwell'));
    });

    test('calls onVoted before firing verdict_viewed event', () {
      final voteIdx = screenText.indexOf('onVoted');
      final verdictIdx = screenText.indexOf('logVerdictViewed');
      expect(voteIdx, greaterThan(0));
      expect(voteIdx, lessThan(verdictIdx),
          reason: 'onVoted must be called before logVerdictViewed');
    });

    test('calls onVerdictViewed after logVerdictViewed', () {
      expect(screenText, contains('onVerdictViewed'));
      expect(screenText, contains('logCompletedJudgmentLoop'));
    });

    test('cancels dwell timer in dispose', () {
      expect(screenText, contains('_meaningfulDwellTimer?.cancel()'));
    });
  });

  group('V53 migration', () {
    late String sql;
    setUpAll(() {
      sql = File('backend/migrations/V53__judgment_loop_events.sql')
          .readAsStringSync();
    });

    test('creates judgment_loop_events table', () {
      expect(sql, contains('CREATE TABLE judgment_loop_events'));
    });

    test('includes all required columns', () {
      expect(sql, contains('device_id'));
      expect(sql, contains('post_id'));
      expect(sql, contains('source'));
      expect(sql, contains('loop_duration_seconds'));
      expect(sql, contains('dwell_seconds'));
    });

    test('includes created_at index', () {
      expect(sql, contains('idx_judgment_loop_events_created_at'));
    });
  });

  group('Backend Program.cs — loop-completed endpoint', () {
    late String programText;
    setUpAll(() {
      programText =
          File('backend/Karar.Api/Program.cs').readAsStringSync();
    });

    test('has loop-completed route', () {
      expect(programText, contains('"/api/v1/analytics/loop-completed"'));
    });

    test('uses LoopCompletedRequest type', () {
      expect(programText, contains('LoopCompletedRequest'));
    });

    test('inserts into judgment_loop_events', () {
      expect(programText, contains('judgment_loop_events'));
    });
  });
}

class _FakeClock {
  _FakeClock(DateTime initial) : _current = initial;
  DateTime _current;

  DateTime now() => _current;
  void advance(Duration duration) => _current = _current.add(duration);
}
