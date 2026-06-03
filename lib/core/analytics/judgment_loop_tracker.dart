import 'dart:async';

enum _LoopStage { impression, reading, voted }

class _LoopState {
  _LoopState({
    required this.postId,
    required this.source,
    required this.startedAt,
  });

  final String postId;
  final String source;
  final DateTime startedAt;
  _LoopStage stage = _LoopStage.impression;
  int dwellSeconds = 0;
}

/// Immutable result returned when a full judgment loop completes.
class CompletedLoop {
  const CompletedLoop({
    required this.postId,
    required this.source,
    required this.dwellSeconds,
    required this.loopDurationSeconds,
    required this.votedBeforeResult,
  });

  final String postId;
  final String source;
  final int dwellSeconds;
  final int loopDurationSeconds;

  /// True when the user voted during this loop session before seeing the verdict.
  final bool votedBeforeResult;
}

/// In-memory state machine for tracking "Completed Judgment Loops".
///
/// Lifecycle: impression → reading (dwell ≥5 s) → voted → verdict_viewed.
/// A loop is only completed when all four transitions occur in order within 30 min.
class JudgmentLoopTracker {
  static const _timeoutMinutes = 30;

  _LoopState? _active;

  // Allows tests to inject a fake clock.
  final DateTime Function() _now;

  JudgmentLoopTracker({DateTime Function()? clock}) : _now = clock ?? DateTime.now;

  /// Call when a post becomes visible (impression).
  void onImpression(String postId, String source) {
    _checkTimeout();
    _active = _LoopState(
      postId: postId,
      source: source,
      startedAt: _now(),
    );
  }

  /// Call after dwell ≥5 s in post detail.
  void onMeaningfulDwell(String postId, int dwellSeconds) {
    _checkTimeout();
    if (_active?.postId != postId) return;
    if (_active!.stage == _LoopStage.impression) {
      _active!.stage = _LoopStage.reading;
    }
    _active!.dwellSeconds = dwellSeconds;
  }

  /// Call when the user casts a vote.
  void onVoted(String postId) {
    _checkTimeout();
    if (_active?.postId != postId) return;
    if (_active!.stage == _LoopStage.reading) {
      _active!.stage = _LoopStage.voted;
    }
  }

  /// Call when the verdict is shown to the user.
  /// Returns a [CompletedLoop] if the full loop was traversed, otherwise null.
  CompletedLoop? onVerdictViewed(String postId) {
    _checkTimeout();
    if (_active?.postId != postId) return null;
    if (_active!.stage != _LoopStage.voted) return null;

    final loop = _active!;
    _active = null;

    return CompletedLoop(
      postId: loop.postId,
      source: loop.source,
      dwellSeconds: loop.dwellSeconds,
      loopDurationSeconds: _now().difference(loop.startedAt).inSeconds,
      votedBeforeResult: true,
    );
  }

  void _checkTimeout() {
    if (_active == null) return;
    if (_now().difference(_active!.startedAt).inMinutes >= _timeoutMinutes) {
      _active = null;
    }
  }

  /// Clears any active loop without completing it (used in tests).
  void reset() => _active = null;

  bool get hasActiveLoop => _active != null;

  String? get activePostId => _active?.postId;
}
