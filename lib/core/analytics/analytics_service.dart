import 'package:firebase_analytics/firebase_analytics.dart';

class AnalyticsService {
  FirebaseAnalytics? _analytics;

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

  Future<void> logSessionEnd({
    required int durationSeconds,
    required int postsViewed,
    required int votesCast,
    required int commentsPosted,
  }) async {
    await _a?.logEvent(
      name: 'session_end',
      parameters: {
        'duration_seconds': durationSeconds,
        'posts_viewed': postsViewed,
        'votes_cast': votesCast,
        'comments_posted': commentsPosted,
      },
    );
  }

  Future<void> logPushNotificationOpened({required String type}) async {
    await _a?.logEvent(
      name: 'push_notification_opened',
      parameters: {'type': type},
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

