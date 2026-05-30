import 'dart:async';

import 'package:firebase_analytics/firebase_analytics.dart';

import '../api/api_client.dart';
import '../api/api_endpoints.dart';

class AnalyticsService {
  AnalyticsService({ApiClient? apiClient}) : _apiClient = apiClient;

  FirebaseAnalytics? _analytics;
  final ApiClient? _apiClient;

  FirebaseAnalytics? get _a {
    try {
      return _analytics ??= FirebaseAnalytics.instance;
    } catch (_) {
      return null;
    }
  }

  Future<void> logPostViewed({
    required String postId,
    required String category,
    String source = 'feed',
  }) async {
    await _a?.logEvent(
      name: 'post_viewed',
      parameters: {
        'post_id': postId,
        'category': category,
        'source': source,
      },
    );
  }

  Future<void> logPostVoted({
    required String voteType,
    required int postAgeHours,
    required bool isRegistered,
  }) async {
    await _a?.logEvent(
      name: 'post_voted',
      parameters: {
        'vote_type': voteType,
        'post_age_hours': postAgeHours,
        'user_type': isRegistered ? 'registered' : 'guest',
      },
    );
  }

  Future<void> logPostCreated({
    required String category,
    required bool hasImage,
    required int contentLength,
    required bool isRegistered,
  }) async {
    await _a?.logEvent(
      name: 'post_created',
      parameters: {
        'category': category,
        'has_image': hasImage,
        'content_length': contentLength,
        'user_type': isRegistered ? 'registered' : 'guest',
      },
    );
  }

  Future<void> logCommentPosted({
    required String category,
    required bool isRegistered,
  }) async {
    await _a?.logEvent(
      name: 'comment_posted',
      parameters: {
        'post_category': category,
        'user_type': isRegistered ? 'registered' : 'guest',
      },
    );
  }

  Future<void> logContentReported({
    required String reason,
    required String targetType,
  }) async {
    await _a?.logEvent(
      name: 'content_reported',
      parameters: {
        'reason': reason,
        'target_type': targetType,
      },
    );
  }

  Future<void> logPostShared({required String postId, String? category}) async {
    await _a?.logEvent(
      name: 'post_shared',
      parameters: {
        'post_id': postId,
        if (category != null) 'category': category,
      },
    );
  }

  Future<void> logSearchPerformed({required String query}) async {
    await _a?.logEvent(
      name: 'search_performed',
      parameters: {'query_length': query.length},
    );
  }

  Future<void> logCreatePostStarted() async {
    await _a?.logEvent(name: 'create_post_started');
  }

  Future<void> logCreatePostCategorySelected({required String category}) async {
    await _a?.logEvent(
      name: 'create_post_category_selected',
      parameters: {'category': category},
    );
  }

  Future<void> logCreatePostContentStarted() async {
    await _a?.logEvent(name: 'create_post_content_started');
  }

  Future<void> logCreatePostSubmitAttempted({
    required String category,
    required int titleLength,
    required int contentLength,
    required bool hasImage,
  }) async {
    await _a?.logEvent(
      name: 'create_post_submit_attempted',
      parameters: {
        'category': category,
        'title_length': titleLength,
        'content_length': contentLength,
        'has_image': hasImage,
      },
    );
  }

  Future<void> logCreatePostRejected({required String reason}) async {
    await _a?.logEvent(
      name: 'create_post_rejected',
      parameters: {'reason': reason},
    );
  }

  Future<void> logCreatePostImageAdded() async {
    await _a?.logEvent(name: 'create_post_image_added');
  }

  Future<void> logCreatePostSubmitted({
    required String category,
    required bool hasImage,
    required int contentLength,
  }) async {
    await _a?.logEvent(
      name: 'create_post_submitted',
      parameters: {
        'category': category,
        'has_image': hasImage,
        'content_length': contentLength,
      },
    );
  }

  Future<void> logRegisterPromptShown() async {
    await _a?.logEvent(name: 'register_prompt_shown');
  }

  Future<void> logRegisterStarted() async {
    await _a?.logEvent(name: 'register_started');
  }

  Future<void> logRegisterCompleted({required String method}) async {
    await _a?.logEvent(
      name: 'register_completed',
      parameters: {'method': method},
    );
  }

  Future<void> logFirstVoteCast({
    required String voteType,
    required int timeToVoteSeconds,
    required int sessionNumber,
    String source = 'post_detail',
  }) async {
    await _a?.logEvent(
      name: 'first_vote_cast',
      parameters: {
        'vote_type': voteType,
        'time_to_vote_seconds': timeToVoteSeconds,
        'session_number': sessionNumber,
        'source': source,
      },
    );
  }

  Future<void> logCreatePostPublished({
    required String category,
    required bool hasImage,
  }) async {
    await _a?.logEvent(
      name: 'create_post_published',
      parameters: {
        'category': category,
        'has_image': hasImage,
      },
    );
  }

  Future<void> logCreatePostAbandoned() async {
    await _a?.logEvent(name: 'create_post_abandoned');
  }

  Future<void> logPostDwellTime({
    required String postId,
    required int durationSeconds,
    required bool wasInteracted,
  }) async {
    await _a?.logEvent(
      name: 'post_dwell_time',
      parameters: {
        'post_id': postId,
        'duration_seconds': durationSeconds,
        'was_interacted': wasInteracted ? 1 : 0,
      },
    );
  }

  Future<void> logAppSessionStarted({
    required int sessionNumber,
    required bool isGuest,
  }) async {
    await _a?.logEvent(
      name: 'app_session_started',
      parameters: {
        'session_number': sessionNumber,
        'user_type': isGuest ? 'guest' : 'registered',
      },
    );
  }

  Future<void> logSessionHeartbeat({
    required int durationSeconds,
    required int postsSeen,
    required int votesCast,
    required int commentsPosted,
    required int postsCreated,
    required int maxFeedPosition,
    required int maxDiscoverPosition,
  }) async {
    await _a?.logEvent(
      name: 'app_session_heartbeat',
      parameters: {
        'duration_seconds': durationSeconds,
        'posts_seen': postsSeen,
        'votes_cast': votesCast,
        'comments_posted': commentsPosted,
        'posts_created': postsCreated,
        'max_feed_position': maxFeedPosition,
        'max_discover_position': maxDiscoverPosition,
      },
    );
  }

  Future<void> logSessionEnd({
    required int durationSeconds,
    required int postsViewed,
    required int votesCast,
    required int commentsPosted,
    int postsCreated = 0,
    int maxFeedPosition = -1,
    int maxDiscoverPosition = -1,
  }) async {
    await _a?.logEvent(
      name: 'session_end',
      parameters: {
        'duration_seconds': durationSeconds,
        'posts_viewed': postsViewed,
        'votes_cast': votesCast,
        'comments_posted': commentsPosted,
        'posts_created': postsCreated,
        'max_feed_position': maxFeedPosition,
        'max_discover_position': maxDiscoverPosition,
      },
    );
  }

  Future<void> logPushNotificationOpened({required String type}) async {
    await _a?.logEvent(
      name: 'push_notification_opened',
      parameters: {'type': type},
    );
  }

  // ── Share Landing Funnel ────────────────────────────────────────────────

  Future<void> logShareLandingOpened({
    required String postId,
    String? referrerCode,
    String source = 'share_link',
    String platform = 'app',
    required bool isGuest,
  }) async {
    await _logFirebaseEventBestEffort(
      name: 'share_landing_opened',
      parameters: {
        'post_id': postId,
        if (referrerCode != null) 'referrer_code': referrerCode,
        'source': source,
        'platform': platform,
        'is_guest': isGuest,
        'user_type': isGuest ? 'guest' : 'registered',
      },
    );
    _postGrowthEvent(
      eventType: 'share_landing_opened',
      postId: postId,
      source: source,
      platform: platform,
      referrerCode: referrerCode,
    );
  }

  Future<void> logShareLandingVoteAttempt({
    required String postId,
    String? referrerCode,
    String source = 'share_link',
    String platform = 'app',
    required bool isGuest,
  }) async {
    await _logFirebaseEventBestEffort(
      name: 'share_landing_vote_attempt',
      parameters: {
        'post_id': postId,
        if (referrerCode != null) 'referrer_code': referrerCode,
        'source': source,
        'platform': platform,
        'is_guest': isGuest,
        'user_type': isGuest ? 'guest' : 'registered',
      },
    );
    _postGrowthEvent(
      eventType: 'share_landing_vote_attempt',
      postId: postId,
      source: source,
      platform: platform,
      referrerCode: referrerCode,
    );
  }

  Future<void> logShareLandingCompletedJudgment({
    required String postId,
    required String voteType,
    String? referrerCode,
    String source = 'share_link',
    String platform = 'app',
    required bool isGuest,
  }) async {
    await _logFirebaseEventBestEffort(
      name: 'share_landing_completed_judgment',
      parameters: {
        'post_id': postId,
        'vote_type': voteType,
        if (referrerCode != null) 'referrer_code': referrerCode,
        'source': source,
        'platform': platform,
        'is_guest': isGuest,
        'user_type': isGuest ? 'guest' : 'registered',
      },
    );
    _postGrowthEvent(
      eventType: 'share_landing_completed_judgment',
      postId: postId,
      source: source,
      platform: platform,
      referrerCode: referrerCode,
    );
  }

  /// Stub for future install attribution
  Future<void> logShareToInstall({
    String? postId,
    String? referrerCode,
    String source = 'share_link',
    String platform = 'app',
    bool? isGuest,
  }) async {
    await _logFirebaseEventBestEffort(
      name: 'share_to_install',
      parameters: {
        if (postId != null) 'post_id': postId,
        if (referrerCode != null) 'referrer_code': referrerCode,
        'source': source,
        'platform': platform,
        if (isGuest != null) 'is_guest': isGuest,
        if (isGuest != null) 'user_type': isGuest ? 'guest' : 'registered',
      },
    );
    _postGrowthEvent(
      eventType: 'share_to_install',
      postId: postId,
      source: source,
      platform: platform,
      referrerCode: referrerCode,
    );
  }

  Future<void> _logFirebaseEventBestEffort({
    required String name,
    Map<String, Object>? parameters,
  }) async {
    try {
      await _a?.logEvent(name: name, parameters: parameters);
    } catch (_) {}
  }

  void _postGrowthEvent({
    required String eventType,
    String? postId,
    String? source,
    String? platform,
    String? referrerCode,
  }) {
    if (_apiClient == null) return;
    unawaited(
      _apiClient.postJson<void>(
        ApiEndpoints.growthEvents,
        body: {
          'eventType': eventType,
          if (postId != null) 'postId': postId,
          if (source != null) 'source': source,
          if (platform != null) 'platform': platform,
          if (referrerCode != null) 'referrerCode': referrerCode,
        },
      ).catchError((_) {}),
    );
  }

  // ── North-Star: Weekly Completed Judgment Loops ─────────────────────────

  Future<void> logVerdictViewed({
    required String postId,
    required String voteType,
    String source = 'post_detail',
    String? rankingReason,
  }) async {
    await _a?.logEvent(
      name: 'verdict_viewed',
      parameters: {
        'post_id': postId,
        'vote_type': voteType,
        'source': source,
        if (rankingReason != null) 'ranking_reason': rankingReason,
      },
    );

    if (source == 'notification') {
      _postGrowthEvent(
        eventType: 'notification_completed_judgment',
        postId: postId,
        source: source,
      );
    } else if (source == 'feed') {
      _postGrowthEvent(
        eventType: 'feed_completed_judgment',
        postId: postId,
        source: source,
      );
    } else if (source == 'search') {
      _postGrowthEvent(
        eventType: 'search_completed_judgment',
        postId: postId,
        source: source,
      );
    }
  }

  Future<void> logDiscoverImpression({
    required String postId,
    required int position,
    String source = 'discover',
    String rankingReason = 'trending',
  }) async {
    await _a?.logEvent(
      name: 'discover_impression',
      parameters: {
        'post_id': postId,
        'source': source,
        'position': position,
        'ranking_reason': rankingReason,
      },
    );
  }

  Future<void> logDiscoverDwell({
    required String postId,
    required int durationSeconds,
    required int position,
    String source = 'discover',
    String rankingReason = 'trending',
  }) async {
    await _a?.logEvent(
      name: 'discover_dwell',
      parameters: {
        'post_id': postId,
        'source': source,
        'duration_seconds': durationSeconds,
        'position': position,
        'ranking_reason': rankingReason,
      },
    );
  }

  Future<void> logDiscoverSkip({
    required String postId,
    required int durationSeconds,
    required int position,
    String source = 'discover',
    String rankingReason = 'trending',
  }) async {
    await _a?.logEvent(
      name: 'discover_skip',
      parameters: {
        'post_id': postId,
        'source': source,
        'duration_seconds': durationSeconds,
        'position': position,
        'ranking_reason': rankingReason,
      },
    );
  }

  Future<void> logDiscoverVote({
    required String postId,
    required String voteType,
    required int position,
    String source = 'discover',
    String rankingReason = 'trending',
  }) async {
    await _a?.logEvent(
      name: 'discover_vote',
      parameters: {
        'post_id': postId,
        'source': source,
        'vote_type': voteType,
        'position': position,
        'ranking_reason': rankingReason,
      },
    );
  }

  Future<void> logDiscoverCommentOpen({
    required String postId,
    required int position,
    String source = 'discover',
    String rankingReason = 'trending',
  }) async {
    await _a?.logEvent(
      name: 'discover_comment_open',
      parameters: {
        'post_id': postId,
        'source': source,
        'position': position,
        'ranking_reason': rankingReason,
      },
    );
  }

  Future<void> logPostNotInterested({
    required String postId,
    String source = 'discover',
    String? rankingReason,
  }) async {
    await _a?.logEvent(
      name: 'post_not_interested',
      parameters: {
        'post_id': postId,
        'source': source,
        if (rankingReason != null) 'ranking_reason': rankingReason,
      },
    );
  }

  Future<void> logFeedScrollDepth({
    required int milestone,
    required int positionReached,
    required String sort,
    String? categoryId,
  }) async {
    await _a?.logEvent(
      name: 'feed_scroll_depth',
      parameters: {
        'milestone': milestone,
        'position_reached': positionReached,
        'sort': sort,
        if (categoryId != null) 'category_id': categoryId,
      },
    );
  }

  Future<void> logDiscoverSnapDepth({
    required int milestone,
    required int positionReached,
  }) async {
    await _a?.logEvent(
      name: 'discover_snap_depth',
      parameters: {
        'milestone': milestone,
        'position_reached': positionReached,
      },
    );
  }

  Future<void> logPostContentScrollDepth({
    required String postId,
    required int milestone,
  }) async {
    await _a?.logEvent(
      name: 'post_content_scroll_depth',
      parameters: {
        'post_id': postId,
        'milestone': milestone,
      },
    );
  }

  Future<void> setAnalyticsEnabled(bool enabled) async {
    await _a?.setAnalyticsCollectionEnabled(enabled);
  }

  Future<void> setUserType(bool isRegistered) async {
    await _a?.setUserProperty(
      name: 'user_type',
      value: isRegistered ? 'registered' : 'guest',
    );
  }
}
