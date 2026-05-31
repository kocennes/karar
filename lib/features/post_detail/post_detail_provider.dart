import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:vibration/vibration.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/data/sample_posts.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/rate_limit_ui.dart';
import '../feed/feed_provider.dart';

class PostDetailState {
  const PostDetailState({
    this.post,
    this.comments = const [],
    this.isLoadingComments = false,
    this.isSubmittingComment = false,
    this.commentSort = 'top',
    this.replyingToComment,
    this.isRefreshingAiSummary = false,
    this.error,
    this.errorCode,
  });

  final Post? post;
  final List<Comment> comments;
  final bool isLoadingComments;
  final bool isSubmittingComment;
  final String commentSort;
  final Comment? replyingToComment;
  final bool isRefreshingAiSummary;
  final String? error;
  final String? errorCode;

  Comment? get topRationale {
    if (comments.isEmpty) return null;
    // Find top level comments (parentId == null), not pinned, with at least 5 upvotes
    final candidates = comments
        .where((c) => c.parentId == null && !c.isPinned && c.upvoteCount >= 5)
        .toList();
    if (candidates.isEmpty) return null;
    // Sort by upvoteCount desc
    candidates.sort((a, b) => b.upvoteCount.compareTo(a.upvoteCount));
    return candidates.first;
  }

  Comment? get balancedRationale {
    if (comments.isEmpty) return null;
    final candidates = comments
        .where((c) => c.parentId == null && !c.isPinned && c.bridgingScore > 0)
        .toList();
    if (candidates.isEmpty) return null;
    candidates.sort((a, b) => b.bridgingScore.compareTo(a.bridgingScore));
    return candidates.first;
  }

  PostDetailState copyWith({
    Post? post,
    List<Comment>? comments,
    bool? isLoadingComments,
    bool? isSubmittingComment,
    String? commentSort,
    Comment? replyingToComment,
    bool? isRefreshingAiSummary,
    bool clearReplyingTo = false,
    String? error,
    String? errorCode,
    bool clearError = false,
  }) =>
      PostDetailState(
        post: post ?? this.post,
        comments: comments ?? this.comments,
        isLoadingComments: isLoadingComments ?? this.isLoadingComments,
        isSubmittingComment: isSubmittingComment ?? this.isSubmittingComment,
        commentSort: commentSort ?? this.commentSort,
        replyingToComment: clearReplyingTo
            ? null
            : (replyingToComment ?? this.replyingToComment),
        isRefreshingAiSummary:
            isRefreshingAiSummary ?? this.isRefreshingAiSummary,
        error: clearError ? null : (error ?? this.error),
        errorCode: clearError ? null : (errorCode ?? this.errorCode),
      );
}

class PostDetailNotifier extends FamilyNotifier<PostDetailState, String> {
  @override
  PostDetailState build(String postId) {
    if (AppRuntime.useRemoteApi) {
      Future.microtask(() => Future.wait([loadPost(), loadComments()]));
      _connectSse();
      return const PostDetailState(isLoadingComments: true);
    }
    return const PostDetailState();
  }

  void _connectSse() {
    final subscription = ref
        .read(postRepositoryProvider)
        .watchPost(_postId)
        .listen(_handleSseEvent, onError: (_) {});
    ref.onDispose(subscription.cancel);
  }

  void _handleSseEvent(SseEvent event) {
    final type = event.type;
    final data = event.data;
    if (type == 'vote_update') {
      final json = jsonDecode(data) as Map<String, dynamic>;
      final post = state.post;
      if (post == null) return;
      final voteCountHakli = json['voteCountHakli'] ?? json['hakli'];
      final voteCountHaksiz = json['voteCountHaksiz'] ?? json['haksiz'];
      if (voteCountHakli is! int || voteCountHaksiz is! int) return;
      state = state.copyWith(
        post: post.copyWith(
          voteCountHakli: voteCountHakli,
          voteCountHaksiz: voteCountHaksiz,
        ),
      );
      ref.read(feedProvider.notifier).updatePost(state.post!);
    } else if (type == 'new_comment') {
      _refreshComments();
    }
  }

  void _refreshComments() {
    if (!AppRuntime.useRemoteApi) return;
    ref
        .read(postRepositoryProvider)
        .fetchComments(_postId, sort: state.commentSort)
        .then((comments) {
      state = state.copyWith(comments: comments);
    }).catchError((_) {});
  }

  String get _postId => arg;

  void setPost(Post post) {
    if (state.post?.id != post.id) {
      state = state.copyWith(post: post);
    }
  }

  void setReplyingTo(Comment comment) {
    state = state.copyWith(replyingToComment: comment);
  }

  void cancelReply() {
    state = state.copyWith(clearReplyingTo: true);
  }

  Future<void> toggleSave() async {
    final current = state.post;
    if (current == null) return;

    final wasSaved = current.isSaved;
    state = state.copyWith(post: current.copyWith(isSaved: !wasSaved));

    if (!AppRuntime.useRemoteApi) return;

    try {
      if (wasSaved) {
        await ref.read(postRepositoryProvider).unsavePost(current.id);
      } else {
        await ref.read(postRepositoryProvider).savePost(current.id);
      }
    } catch (_) {
      state = state.copyWith(post: current);
    }
  }

  Future<void> loadPost() async {
    if (!AppRuntime.useRemoteApi) {
      final post = samplePosts.firstWhere((p) => p.id == _postId,
          orElse: () => samplePosts.first);
      state = state.copyWith(post: post);
      return;
    }
    try {
      final post = await ref.read(postRepositoryProvider).fetchPost(_postId);
      state = state.copyWith(post: post, clearError: true);

      ref.read(analyticsServiceProvider).logPostViewed(
            postId: post.id,
            category: post.category.name,
          );
    } on ApiException catch (e) {
      state = state.copyWith(error: e.friendlyMessage, errorCode: e.code);
    } catch (_) {
      state = state.copyWith(error: 'Gönderi yüklenemedi.');
    }
  }

  Future<void> loadComments() async {
    if (!AppRuntime.useRemoteApi) {
      final post = samplePosts.firstWhere((p) => p.id == _postId,
          orElse: () => samplePosts.first);
      state = state.copyWith(comments: post.comments, isLoadingComments: false);
      return;
    }
    state = state.copyWith(isLoadingComments: true, clearError: true);
    try {
      final comments = await ref.read(performanceServiceProvider).trace(
            'comment_panel_open',
            () => ref
                .read(postRepositoryProvider)
                .fetchComments(_postId, sort: state.commentSort),
          );
      state = state.copyWith(comments: comments, isLoadingComments: false);
    } on ApiException catch (e) {
      state =
          state.copyWith(isLoadingComments: false, error: e.friendlyMessage);
    } catch (_) {
      state = state.copyWith(
        isLoadingComments: false,
        error: 'Yorumlar yüklenemedi.',
      );
    }
  }

  Future<void> setCommentSort(String sort) async {
    if (sort == state.commentSort) return;
    state = state.copyWith(commentSort: sort);

    if (!AppRuntime.useRemoteApi) {
      final sorted = [...state.comments]..sort((a, b) {
          return switch (sort) {
            'new' => b.createdAgo.compareTo(a.createdAgo),
            'old' => a.createdAgo.compareTo(b.createdAgo),
            'controversial' =>
              _controversyScore(b).compareTo(_controversyScore(a)),
            _ => _wilsonScore(b).compareTo(_wilsonScore(a)),
          };
        });
      state = state.copyWith(comments: sorted);
      return;
    }

    await loadComments();
  }

  double _wilsonScore(Comment c, {int depth = 0}) {
    // Reddit-style Wilson Lower Bound (simplified for mock)
    // Using laplace correction: upvotes+1, total+2
    final ups = c.upvoteCount + 1;
    final n = c.upvoteCount + 2;
    final p = ups / n;
    const z = 1.281;
    final left = p + (z * z) / (2 * n);
    final right = z * sqrt((p * (1 - p) + (z * z) / (4 * n)) / n);
    final under = 1 + (z * z) / n;

    double score = (left - right) / under;

    // Phase 3: Discussion Quality Score integration
    // Bonus for length (max 20% bonus for 200+ characters)
    final lengthBonus = (c.content.length / 1000).clamp(0.0, 0.2);
    score *= (1 + lengthBonus);

    // Bonus for depth (encourages meaningful sub-discussions)
    if (depth > 0) {
      score *= (1 + (depth * 0.05).clamp(0.0, 0.15));
    }

    return score;
  }

  double _controversyScore(Comment c) {
    // Simplified controversy for mock
    // In real app: POWER(total, 0.5) * (MIN(up, down) / MAX(up, down))
    return c.upvoteCount
        .toDouble(); // Placeholder since we lack comment downvotes in model
  }

  Future<void> vote(String voteType) async {
    final current = state.post;
    if (current == null) return;

    if (!AppRuntime.useRemoteApi) {
      state = state.copyWith(
          post: _applyVote(current, VoteType.values.byName(voteType)));
      _triggerHaptic();
      return;
    }

    final optimistic = _applyVote(current, VoteType.values.byName(voteType));
    state = state.copyWith(post: optimistic);
    _triggerHaptic();

    try {
      final vt = VoteType.values.byName(voteType);
      final updated = await ref.read(performanceServiceProvider).trace(
            'vote_submit',
            () => ref.read(postRepositoryProvider).vote(current.id, vt),
          );
      state = state.copyWith(post: updated);
      ref.read(feedProvider.notifier).updatePost(updated);

      ref.read(analyticsServiceProvider).logPostVoted(
            voteType: voteType,
            postAgeHours: _postAgeHours(current),
            isRegistered: ref.read(authServiceProvider).isLoggedIn,
          );

      final ratingService = ref.read(ratingServiceProvider);
      await ratingService.logVote();
      await ratingService.maybeRequestRating();
    } on ApiException catch (e) {
      state = state.copyWith(
        post: current,
        error: RateLimitUi.messageFor(e, RateLimitedAction.vote),
      );
    } catch (e) {
      state = state.copyWith(post: current, error: 'Oy gönderilemedi.');
    }
  }

  Future<void> votePoll(String optionId) async {
    final current = state.post;
    if (current == null || current.poll == null) return;

    if (!AppRuntime.useRemoteApi) {
      final updatedPoll = current.poll!.copyWith(
        mySelectionId: optionId,
        totalVotes: current.poll!.totalVotes + 1,
        options: current.poll!.options.map((o) {
          if (o.id == optionId) return o.copyWith(voteCount: o.voteCount + 1);
          return o;
        }).toList(),
      );
      state = state.copyWith(post: current.copyWith(poll: updatedPoll));
      _triggerHaptic();
      return;
    }

    try {
      final updated =
          await ref.read(postRepositoryProvider).votePoll(current.id, optionId);
      state = state.copyWith(post: updated);
      ref.read(feedProvider.notifier).updatePost(updated);
      _triggerHaptic();
    } on ApiException catch (e) {
      state = state.copyWith(error: e.friendlyMessage);
    } catch (_) {
      state = state.copyWith(error: 'Oy gönderilemedi.');
    }
  }

  Future<void> removeVote() async {
    final current = state.post;
    if (current == null) return;

    if (!AppRuntime.useRemoteApi) {
      state = state.copyWith(post: current.copyWith(clearVote: true));
      return;
    }

    state = state.copyWith(post: current.copyWith(clearVote: true));
    try {
      final updated =
          await ref.read(postRepositoryProvider).removeVote(current.id);
      state = state.copyWith(post: updated);
      ref.read(feedProvider.notifier).updatePost(updated);
    } catch (_) {
      state = state.copyWith(post: current, error: 'Oy kaldırılamadı.');
    }
  }

  Future<void> submitComment(String content) async {
    final post = state.post;
    if (post == null) return;
    final parentId = state.replyingToComment?.id;
    state = state.copyWith(isSubmittingComment: true, clearError: true);

    if (!AppRuntime.useRemoteApi) {
      final comment = Comment(
        id: DateTime.now().microsecondsSinceEpoch.toString(),
        content: content,
        upvoteCount: 0,
        createdAgo: 'şimdi',
        isOwner: true,
        authorName: ref.read(currentUserProvider)?.username ?? 'Sen',
        parentId: parentId,
      );

      if (parentId != null) {
        _refreshComments(); // Simplified for mock
      } else {
        state = state.copyWith(
          comments: [comment, ...state.comments],
        );
      }

      state = state.copyWith(
        isSubmittingComment: false,
        clearReplyingTo: true,
        post: post.copyWith(commentCount: post.commentCount + 1),
      );
      return;
    }

    try {
      final comment = await ref
          .read(postRepositoryProvider)
          .createComment(_postId, content, parentId: parentId);

      if (parentId != null) {
        _refreshComments();
      } else {
        state = state.copyWith(
          comments: [comment, ...state.comments],
        );
      }

      state = state.copyWith(
        isSubmittingComment: false,
        clearReplyingTo: true,
        post: post.copyWith(commentCount: post.commentCount + 1),
      );

      ref.read(analyticsServiceProvider).logCommentPosted(
            category: post.category.name,
            isRegistered: ref.read(authServiceProvider).isLoggedIn,
          );
    } on ApiException catch (e) {
      state = state.copyWith(
        isSubmittingComment: false,
        error: RateLimitUi.messageFor(e, RateLimitedAction.comment),
      );
    } catch (_) {
      state = state.copyWith(
        isSubmittingComment: false,
        error: 'Yorum gönderilemedi.',
      );
    }
  }

  Future<void> upvoteComment(Comment comment) async {
    final optimistic = comment.copyWith(
      myUpvote: !comment.myUpvote,
      upvoteCount:
          comment.myUpvote ? comment.upvoteCount - 1 : comment.upvoteCount + 1,
      // If was downvoted, remove it
      myDownvote: false,
      downvoteCount: comment.myDownvote ? comment.downvoteCount - 1 : comment.downvoteCount,
    );
    _replaceComment(comment, optimistic);

    if (!AppRuntime.useRemoteApi) return;

    try {
      final updated = comment.myUpvote
          ? await ref.read(postRepositoryProvider).removeCommentUpvote(comment)
          : await ref.read(postRepositoryProvider).upvoteComment(comment);
      _replaceComment(optimistic, updated);
    } catch (_) {
      _replaceComment(optimistic, comment);
    }
  }

  Future<void> downvoteComment(Comment comment) async {
    final optimistic = comment.copyWith(
      myDownvote: !comment.myDownvote,
      downvoteCount:
          comment.myDownvote ? comment.downvoteCount - 1 : comment.downvoteCount + 1,
      // If was upvoted, remove it
      myUpvote: false,
      upvoteCount: comment.myUpvote ? comment.upvoteCount - 1 : comment.upvoteCount,
    );
    _replaceComment(comment, optimistic);

    if (!AppRuntime.useRemoteApi) return;

    try {
      final updated = comment.myDownvote
          ? await ref.read(postRepositoryProvider).removeCommentDownvote(comment)
          : await ref.read(postRepositoryProvider).downvoteComment(comment);
      _replaceComment(optimistic, updated);
    } catch (_) {
      _replaceComment(optimistic, comment);
    }
  }

  void _replaceComment(Comment old, Comment replacement) {
    final index = state.comments.indexWhere((c) => c.id == old.id);
    if (index == -1) return;
    state = state.copyWith(
      comments: List.of(state.comments)..[index] = replacement,
    );
  }

  Future<void> deleteComment(Comment comment) async {
    if (!AppRuntime.useRemoteApi) {
      _removeComment(comment);
      return;
    }
    try {
      await ref.read(postRepositoryProvider).deleteComment(comment.id);
      _removeComment(comment);
    } on ApiException catch (e) {
      state = state.copyWith(error: e.friendlyMessage);
    } catch (_) {
      state = state.copyWith(error: 'Yorum silinemedi.');
    }
  }

  Future<void> editComment(Comment comment, String newContent) async {
    final original = comment.content;
    _replaceComment(
        comment, comment.copyWith(content: newContent, isEdited: true));

    if (!AppRuntime.useRemoteApi) return;

    try {
      await ref
          .read(postRepositoryProvider)
          .updateComment(comment.id, newContent);
    } catch (_) {
      _replaceComment(comment.copyWith(content: newContent),
          comment.copyWith(content: original));
      state = state.copyWith(error: 'Yorum güncellenemedi.');
    }
  }

  Future<void> reactToComment(Comment comment, String emoji) async {
    if (!AppRuntime.useRemoteApi) {
      final updated = comment.copyWith(
        myReaction: emoji,
        reactions: Map<String, int>.from(comment.reactions)
          ..update(emoji, (v) => v + 1, ifAbsent: () => 1),
      );
      _replaceComment(comment, updated);
      _triggerHaptic();
      return;
    }

    try {
      final updated = await ref
          .read(postRepositoryProvider)
          .reactToComment(comment.id, emoji);
      _replaceComment(comment, updated);
      _triggerHaptic();
    } catch (_) {
      state = state.copyWith(error: 'Tepki gönderilemedi.');
    }
  }

  Future<void> removeCommentReaction(Comment comment) async {
    if (!AppRuntime.useRemoteApi) {
      if (comment.myReaction == null) return;
      final updated = comment.copyWith(
        clearMyReaction: true,
        reactions: Map<String, int>.from(comment.reactions)
          ..update(comment.myReaction!, (v) => (v - 1).clamp(0, 999999)),
      );
      _replaceComment(comment, updated);
      return;
    }

    try {
      final updated = await ref
          .read(postRepositoryProvider)
          .removeCommentReaction(comment.id);
      _replaceComment(comment, updated);
    } catch (_) {
      state = state.copyWith(error: 'Tepki kaldırılamadı.');
    }
  }

  Future<void> pinComment(Comment comment) async {
    if (!AppRuntime.useRemoteApi) return;
    final postId = state.post?.id;
    if (postId == null) return;

    // Optimistic: unpin all, pin target
    final updated = state.comments.map((c) {
      if (c.id == comment.id) return c.copyWith(isPinned: true);
      if (c.isPinned) return c.copyWith(isPinned: false);
      return c;
    }).toList();
    // Move pinned to front
    updated.sort((a, b) {
      if (a.isPinned) return -1;
      if (b.isPinned) return 1;
      return 0;
    });
    state = state.copyWith(comments: updated);

    try {
      await ref.read(postRepositoryProvider).pinComment(postId, comment.id);
    } catch (_) {
      await loadComments();
      state = state.copyWith(error: 'Yorum sabitlenemedi.');
    }
  }

  Future<void> unpinComment() async {
    if (!AppRuntime.useRemoteApi) return;
    final postId = state.post?.id;
    if (postId == null) return;

    final updated =
        state.comments.map((c) => c.copyWith(isPinned: false)).toList();
    state = state.copyWith(comments: updated);

    try {
      await ref.read(postRepositoryProvider).unpinComment(postId);
    } catch (_) {
      await loadComments();
      state = state.copyWith(error: 'Sabit kaldırılamadı.');
    }
  }

  Future<void> refreshAiSummary() async {
    final post = state.post;
    if (post == null) return;

    state = state.copyWith(isRefreshingAiSummary: true, clearError: true);

    if (!AppRuntime.useRemoteApi) {
      await Future<void>.delayed(const Duration(seconds: 1));
      state = state.copyWith(
        isRefreshingAiSummary: false,
        post: post.copyWith(
          aiSummary:
              'Topluluk çoğunlukla hafta sonu mesaisinin zorunlu olamayacağı görüşünde birleşiyor. '
              'Yöneticinin tutumu ise profesyonellikten uzak bulunmuş.',
        ),
      );
      return;
    }

    try {
      final updated =
          await ref.read(postRepositoryProvider).refreshAiSummary(post.id);
      state = state.copyWith(post: updated, isRefreshingAiSummary: false);
    } on ApiException catch (e) {
      state = state.copyWith(
        isRefreshingAiSummary: false,
        error: RateLimitUi.messageFor(e, RateLimitedAction.comment),
      );
    } catch (_) {
      state = state.copyWith(
        isRefreshingAiSummary: false,
        error: 'Özet yenilenemedi.',
      );
    }
  }

  Future<void> editPost(String title, String content) async {
    final current = state.post;
    if (current == null) return;

    state = state.copyWith(
        post: current.copyWith(title: title, content: content, isEdited: true));

    if (!AppRuntime.useRemoteApi) return;

    try {
      await ref
          .read(postRepositoryProvider)
          .updatePost(current.id, title, content);
      ref.read(feedProvider.notifier).updatePost(state.post!);
    } catch (_) {
      state = state.copyWith(post: current, error: 'Gönderi güncellenemedi.');
    }
  }

  Future<bool> deletePost() async {
    final current = state.post;
    if (current == null) return false;

    if (!AppRuntime.useRemoteApi) {
      ref.read(feedProvider.notifier).removePost(current.id);
      return true;
    }

    try {
      await ref.read(postRepositoryProvider).deletePost(current.id);
      ref.read(feedProvider.notifier).removePost(current.id);
      return true;
    } catch (_) {
      state = state.copyWith(error: 'Gönderi silinemedi.');
      return false;
    }
  }

  void _removeComment(Comment comment) {
    final post = state.post;
    state = state.copyWith(
      comments: state.comments.where((c) => c.id != comment.id).toList(),
      post: post?.copyWith(
        commentCount: (post.commentCount - 1).clamp(0, 999999),
      ),
    );
  }

  void clearError() {
    state = state.copyWith(clearError: true);
  }

  Future<void> _triggerHaptic() async {
    if (kIsWeb) return;
    if (await Vibration.hasVibrator()) {
      Vibration.vibrate(duration: 50, amplitude: 128);
    }
  }

  Post _applyVote(Post post, VoteType vt) {
    final old = post.myVote;
    var hakli = post.voteCountHakli;
    var haksiz = post.voteCountHaksiz;
    if (old == VoteType.hakli) hakli--;
    if (old == VoteType.haksiz) haksiz--;
    if (vt == VoteType.hakli) hakli++;
    if (vt == VoteType.haksiz) haksiz++;
    return post.copyWith(
      voteCountHakli: hakli,
      voteCountHaksiz: haksiz,
      myVote: vt,
    );
  }

  int _postAgeHours(Post post) {
    final age = DateTime.now().difference(post.createdAt.toLocal()).inHours;
    return age < 0 ? 0 : age;
  }
}

final postDetailProvider =
    NotifierProvider.family<PostDetailNotifier, PostDetailState, String>(
        PostDetailNotifier.new);
