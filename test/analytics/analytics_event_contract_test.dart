import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// Contract tests for the North-Star analytics event chain.
///
/// Goal: verify that every step of a "Completed Judgment Loop" is instrumented:
///   discover_impression → discover_dwell/skip → discover_vote → verdict_viewed
///
/// Tests are source-file assertions — fast, no mock setup, no Firebase needed.
/// They break the build if a method is renamed or a call is removed accidentally.
void main() {
  // ── Analytics service contract ─────────────────────────────────────────────

  group('AnalyticsService — North-Star event methods exist', () {
    late String serviceText;

    setUpAll(() {
      serviceText =
          File('lib/core/analytics/analytics_service.dart').readAsStringSync();
    });

    test('has verdict_viewed method', () {
      expect(serviceText, contains('logVerdictViewed'),
          reason: 'verdict_viewed is the terminal event of a judgment loop');
    });

    test('has discover_impression method', () {
      expect(serviceText, contains('logDiscoverImpression'));
    });

    test('has discover_dwell method', () {
      expect(serviceText, contains('logDiscoverDwell'));
    });

    test('has discover_skip method', () {
      expect(serviceText, contains('logDiscoverSkip'));
    });

    test('has discover_vote method', () {
      expect(serviceText, contains('logDiscoverVote'));
    });

    test('has discover_comment_open method', () {
      expect(serviceText, contains('logDiscoverCommentOpen'));
    });

    test('has post_not_interested method', () {
      expect(serviceText, contains('logPostNotInterested'));
    });

    test('has share_landing_opened method', () {
      expect(serviceText, contains('logShareLandingOpened'));
    });

    test('has share_landing_vote_attempt method', () {
      expect(serviceText, contains('logShareLandingVoteAttempt'));
    });

    test('has share_landing_completed_judgment method', () {
      expect(serviceText, contains('logShareLandingCompletedJudgment'));
    });

    test('has share_to_install stub', () {
      expect(serviceText, contains('logShareToInstall'));
    });
  });

  // ── Parameter contract ─────────────────────────────────────────────────────

  group('AnalyticsService — event parameters include required fields', () {
    late String serviceText;

    setUpAll(() {
      serviceText =
          File('lib/core/analytics/analytics_service.dart').readAsStringSync();
    });

    test("parameters include 'post_id'", () {
      expect(serviceText, contains("'post_id'"));
    });

    test("parameters include 'source'", () {
      expect(serviceText, contains("'source'"));
    });

    test("parameters include 'ranking_reason'", () {
      expect(serviceText, contains("'ranking_reason'"),
          reason:
              'ranking_reason links event to recommendation algorithm for analysis');
    });

    test("parameters include 'position'", () {
      expect(serviceText, contains("'position'"),
          reason: 'position enables scroll-depth analysis in Discover feed');
    });

    test("parameters include 'duration_seconds'", () {
      expect(serviceText, contains("'duration_seconds'"));
    });

    test("parameters include 'vote_type'", () {
      expect(serviceText, contains("'vote_type'"));
    });

    test("parameters include 'referrer_code'", () {
      expect(serviceText, contains("'referrer_code'"));
    });

    test("parameters include 'user_type'", () {
      expect(serviceText, contains("'user_type'"));
    });

    test("parameters include 'is_guest'", () {
      expect(serviceText, contains("'is_guest'"));
    });

    test("parameters include 'platform'", () {
      expect(serviceText, contains("'platform'"));
    });
  });

  group('AnalyticsService — share landing funnel contract', () {
    late String serviceText;

    setUpAll(() {
      serviceText =
          File('lib/core/analytics/analytics_service.dart').readAsStringSync();
    });

    test('share landing events include attribution and identity fields', () {
      for (final method in [
        'logShareLandingOpened',
        'logShareLandingVoteAttempt',
        'logShareLandingCompletedJudgment',
        'logShareToInstall',
      ]) {
        final block = _methodBlock(serviceText, method);
        expect(block, contains("'source'"), reason: '$method needs source');
        expect(block, contains("'platform'"), reason: '$method needs platform');
        expect(block, contains("'referrer_code'"),
            reason: '$method needs share attribution');
      }
    });

    test('share landing conversion events include guest state', () {
      for (final method in [
        'logShareLandingOpened',
        'logShareLandingVoteAttempt',
        'logShareLandingCompletedJudgment',
      ]) {
        final block = _methodBlock(serviceText, method);
        expect(block, contains("'is_guest'"), reason: '$method needs is_guest');
        expect(block, contains("'user_type'"),
            reason: '$method needs user_type');
      }
    });

    test('share landing dual-write is best-effort', () {
      expect(serviceText, contains('ApiEndpoints.growthEvents'),
          reason: 'share funnel events must be mirrored to backend');
      expect(serviceText, contains('_logFirebaseEventBestEffort'),
          reason:
              'Firebase analytics failure must not block backend mirroring');
      expect(serviceText, contains('.catchError((_) {})'),
          reason: 'backend growth event write must not affect user flow');
    });
  });

  // ── Discover screen call sites ─────────────────────────────────────────────

  group('DiscoverScreen — fires analytics events at correct call sites', () {
    late String screenText;

    setUpAll(() {
      screenText =
          File('lib/features/feed/discover_screen.dart').readAsStringSync();
    });

    test('fires discover_impression on page enter', () {
      expect(screenText, contains('logDiscoverImpression'),
          reason: '_handlePageEnter must fire impression for ranking analysis');
    });

    test('fires discover_dwell on meaningful engagement (≥3 s)', () {
      expect(screenText, contains('logDiscoverDwell'),
          reason: '_handlePageLeave dwell branch must fire dwell event');
    });

    test('fires discover_skip on fast swipe (<3 s)', () {
      expect(screenText, contains('logDiscoverSkip'),
          reason: '_handlePageLeave skip branch must fire skip event');
    });

    test('fires discover_comment_open when comment sheet opens', () {
      expect(screenText, contains('logDiscoverCommentOpen'));
    });

    test('fires post_not_interested on ilgilenmiyorum tap', () {
      expect(screenText, contains('logPostNotInterested'));
    });

    test('has impression spam protection via _activePostId', () {
      expect(screenText, contains('_activePostId'),
          reason:
              'same post must not generate multiple impression events per visit');
    });

    test('has first-load impression guard via _firstImpressionSent', () {
      expect(screenText, contains('_firstImpressionSent'),
          reason:
              'feed load must not fire impression before first real render');
    });
  });

  // ── Discover feed provider ─────────────────────────────────────────────────

  group('DiscoverFeedProvider — fires discover_vote on cast', () {
    test('logDiscoverVote called in vote() method', () {
      final text = File('lib/features/feed/discover_feed_provider.dart')
          .readAsStringSync();
      expect(text, contains('logDiscoverVote'),
          reason: 'voting from Discover feed must be tracked separately from '
              'feed votes for ranking signal attribution');
    });

    test('logVerdictViewed called after discover vote', () {
      final text = File('lib/features/feed/discover_feed_provider.dart')
          .readAsStringSync();
      expect(text, contains('logVerdictViewed'),
          reason: 'Discover voting shows the result inline, so it must close '
              'the Completed Judgment Loop with verdict_viewed');
      expect(text, contains("source: 'discover'"),
          reason: 'verdict_viewed must be source-attributed to Discover');
    });
  });

  // ── Post detail screen ─────────────────────────────────────────────────────

  group('PostDetailScreen — fires verdict_viewed on vote cast', () {
    late String screenText;

    setUpAll(() {
      screenText = File('lib/features/post_detail/post_detail_screen.dart')
          .readAsStringSync();
    });

    test('fires verdict_viewed when myVote changes', () {
      expect(screenText, contains('logVerdictViewed'),
          reason: 'verdict_viewed is the terminal event of a judgment loop; '
              'it must fire whenever a vote is cast or changed');
    });

    test('verdict_viewed is fired in the vote-change listener block', () {
      // Verify temporal proximity: logVerdictViewed must appear after the
      // myVote != null guard and before tracker.incrementVoteCast().
      final verdictIdx = screenText.indexOf('logVerdictViewed');
      final trackerIdx = screenText.indexOf('tracker.incrementVoteCast');
      expect(verdictIdx, greaterThan(0),
          reason: 'logVerdictViewed must be present');
      expect(verdictIdx, lessThan(trackerIdx),
          reason:
              'logVerdictViewed must fire before session tracker increment');
    });

    test('fires share_landing_opened when referrerCode is present', () {
      expect(screenText, contains('logShareLandingOpened'));
    });

    test('fires share_landing_vote_attempt on vote button tap', () {
      expect(screenText, contains('logShareLandingVoteAttempt'));
    });

    test(
        'fires share_landing_completed_judgment when vote cast from share landing',
        () {
      expect(screenText, contains('logShareLandingCompletedJudgment'));
    });
  });

  // ── Feed & Search source attribution ──────────────────────────────────────

  group('Feed source attribution chain', () {
    test('FeedScreen navigates with source=feed query parameter', () {
      final text =
          File('lib/features/feed/feed_screen.dart').readAsStringSync();
      expect(text, contains("source=feed"),
          reason:
              'FeedScreen must tag post navigation with source=feed so that '
              'verdict_viewed events can be attributed to feed in the north-star metric');
    });
  });

  group('Search source attribution chain', () {
    test('SearchScreen navigates with source=search query parameter', () {
      final text =
          File('lib/features/search/search_screen.dart').readAsStringSync();
      expect(text, contains("source=search"),
          reason: 'SearchScreen must tag post navigation with source=search so '
              'that verdict_viewed events can be attributed to search');
    });
  });

  group('AnalyticsService — feed and search dual-write to backend', () {
    late String serviceText;

    setUpAll(() {
      serviceText =
          File('lib/core/analytics/analytics_service.dart').readAsStringSync();
    });

    test('dual-writes feed_completed_judgment for feed source', () {
      expect(serviceText, contains('feed_completed_judgment'),
          reason: "logVerdictViewed must write feed_completed_judgment to "
              "growth_events when source == 'feed'");
      expect(serviceText, contains("source == 'feed'"),
          reason: 'dual-write must be conditional on feed source');
    });

    test('dual-writes search_completed_judgment for search source', () {
      expect(serviceText, contains('search_completed_judgment'),
          reason: "logVerdictViewed must write search_completed_judgment to "
              "growth_events when source == 'search'");
      expect(serviceText, contains("source == 'search'"),
          reason: 'dual-write must be conditional on search source');
    });
  });

  group('PostDetailScreen — logPostViewed receives source', () {
    test('logPostViewed passes widget.source to attribute views by surface',
        () {
      final text = File('lib/features/post_detail/post_detail_screen.dart')
          .readAsStringSync();
      expect(text, contains("source: widget.source ?? 'feed'"),
          reason: 'logPostViewed must carry source so that the full '
              'impression→view→vote funnel can be broken down by surface');
    });
  });

  // ── Notification → Completed Judgment attribution ─────────────────────────

  group('Notification source attribution chain', () {
    test('app.dart tags post navigation from push with source=notification',
        () {
      final text = File('lib/app.dart').readAsStringSync();
      final helper = File('lib/core/notifications/notification_deep_link.dart')
          .readAsStringSync();
      expect(text, contains('NotificationDeepLink.fromPayload'),
          reason:
              '_onMessageOpened must normalize post deep links before navigation');
      expect(helper, contains("query['source'] = 'notification'"),
          reason: 'post deep links opened from push must preserve source');
      expect(helper, contains('uri.replace(queryParameters: query)'),
          reason:
              'notification source tagging must preserve existing query params '
              'like commentId/ref without duplicate source keys');
    });

    test('app_router.dart reads source param and passes to PostDetailScreen',
        () {
      final text = File('lib/core/router/app_router.dart').readAsStringSync();
      expect(text, contains("queryParameters['source']"),
          reason: 'router must extract source param from the URL');
      expect(text, contains('source: source'),
          reason: 'router must forward source to PostDetailScreen');
    });

    test('PostDetailScreen declares source field', () {
      final text = File('lib/features/post_detail/post_detail_screen.dart')
          .readAsStringSync();
      expect(text, contains('this.source'),
          reason: 'PostDetailScreen must declare the source parameter');
    });

    test('PostDetailScreen passes widget.source to logVerdictViewed', () {
      final text = File('lib/features/post_detail/post_detail_screen.dart')
          .readAsStringSync();
      expect(text, contains('source: widget.source'),
          reason: "logVerdictViewed must receive source from widget so that "
              "notification-originated votes are correctly attributed");
    });

    test(
        'AnalyticsService fires notification_completed_judgment when source is notification',
        () {
      final text =
          File('lib/core/analytics/analytics_service.dart').readAsStringSync();
      expect(text, contains('notification_completed_judgment'),
          reason:
              'logVerdictViewed must dual-write notification_completed_judgment '
              "to growth_events when source == 'notification'");
      expect(text, contains("source == 'notification'"),
          reason: 'dual-write must be conditional on notification source only');
    });
  });
}

String _methodBlock(String text, String methodName) {
  final start = text.indexOf(methodName);
  expect(start, greaterThanOrEqualTo(0), reason: '$methodName must exist');
  final nextMethod =
      text.indexOf('\n  Future<void>', start + methodName.length);
  return nextMethod == -1
      ? text.substring(start)
      : text.substring(start, nextMethod);
}
