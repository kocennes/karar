import 'package:shared_preferences/shared_preferences.dart';

class SessionTracker {
  SessionTracker._();

  static const _kSessionCountKey = 'analytics_session_count';
  static const _kFirstVoteLoggedKey = 'analytics_first_vote_logged';
  static const _kNudgeShownKey = 'analytics_conversion_nudge_shown';

  final _stopwatch = Stopwatch()..start();
  int _postsViewed = 0;
  int _votesCast = 0;
  int _commentsPosted = 0;
  int _postsCreated = 0;
  int _maxFeedPosition = -1;
  int _maxDiscoverPosition = -1;
  int _sessionNumber = 1;

  static Future<SessionTracker> create() async {
    final tracker = SessionTracker._();
    await tracker._incrementSessionCount();
    return tracker;
  }

  // For unit tests only — skips SharedPreferences initialisation.
  static SessionTracker testInstance() => SessionTracker._();

  Future<void> _incrementSessionCount() async {
    final prefs = await SharedPreferences.getInstance();
    _sessionNumber = (prefs.getInt(_kSessionCountKey) ?? 0) + 1;
    await prefs.setInt(_kSessionCountKey, _sessionNumber);
  }

  int get sessionNumber => _sessionNumber;
  int get elapsedSeconds => _stopwatch.elapsed.inSeconds;
  int get votesCastInSession => _votesCast;

  void incrementPostViewed() => _postsViewed++;
  void incrementVoteCast() => _votesCast++;
  void incrementCommentPosted() => _commentsPosted++;
  void incrementPostCreated() => _postsCreated++;

  void updateMaxFeedPosition(int pos) {
    if (pos > _maxFeedPosition) _maxFeedPosition = pos;
  }

  void updateMaxDiscoverPosition(int pos) {
    if (pos > _maxDiscoverPosition) _maxDiscoverPosition = pos;
  }

  SessionStats snapshot() => SessionStats(
        durationSeconds: _stopwatch.elapsed.inSeconds,
        postsViewed: _postsViewed,
        votesCast: _votesCast,
        commentsPosted: _commentsPosted,
        postsCreated: _postsCreated,
        maxFeedPosition: _maxFeedPosition,
        maxDiscoverPosition: _maxDiscoverPosition,
      );

  SessionStats flush() {
    final stats = snapshot();
    _stopwatch.reset();
    _stopwatch.start();
    _postsViewed = 0;
    _votesCast = 0;
    _commentsPosted = 0;
    _postsCreated = 0;
    _maxFeedPosition = -1;
    _maxDiscoverPosition = -1;
    return stats;
  }

  Future<bool> isFirstVote() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getBool(_kFirstVoteLoggedKey) != true;
  }

  Future<void> markFirstVoteLogged() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_kFirstVoteLoggedKey, true);
  }

  Future<bool> shouldShowConversionNudge() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getBool(_kNudgeShownKey) != true;
  }

  Future<void> markNudgeShown() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_kNudgeShownKey, true);
  }
}

class SessionStats {
  const SessionStats({
    required this.durationSeconds,
    required this.postsViewed,
    required this.votesCast,
    required this.commentsPosted,
    this.postsCreated = 0,
    this.maxFeedPosition = -1,
    this.maxDiscoverPosition = -1,
  });

  final int durationSeconds;
  final int postsViewed;
  final int votesCast;
  final int commentsPosted;
  final int postsCreated;
  final int maxFeedPosition;
  final int maxDiscoverPosition;
}
