import 'package:in_app_review/in_app_review.dart';
import 'package:shared_preferences/shared_preferences.dart';

class RatingService {
  final InAppReview _inAppReview = InAppReview.instance;

  static const _kSessionCountKey = 'rating_session_count';
  static const _kVoteCountKey = 'rating_vote_count';
  static const _kAskedKey = 'rating_asked';
  static const _kSessionThreshold = 5;
  static const _kVoteThreshold = 3;

  // Called once per app start (from AppServices.create)
  Future<void> incrementSession() async {
    final prefs = await SharedPreferences.getInstance();
    final count = (prefs.getInt(_kSessionCountKey) ?? 0) + 1;
    await prefs.setInt(_kSessionCountKey, count);
  }

  // Called after each vote cast
  Future<void> logVote() async {
    final prefs = await SharedPreferences.getInstance();
    final count = (prefs.getInt(_kVoteCountKey) ?? 0) + 1;
    await prefs.setInt(_kVoteCountKey, count);
  }

  // Called after verdict banner animation completes (post-vote happy moment)
  Future<void> maybeRequestRating() async {
    final prefs = await SharedPreferences.getInstance();

    // Once-only: never ask again after the first request
    if (prefs.getBool(_kAskedKey) == true) return;

    final sessionCount = prefs.getInt(_kSessionCountKey) ?? 0;
    final voteCount = prefs.getInt(_kVoteCountKey) ?? 0;

    if (sessionCount >= _kSessionThreshold && voteCount >= _kVoteThreshold) {
      if (await _inAppReview.isAvailable()) {
        await _inAppReview.requestReview();
        await prefs.setBool(_kAskedKey, true);
      }
    }
  }
}
