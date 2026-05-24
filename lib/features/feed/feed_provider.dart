import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../core/history/history_provider.dart';
import '../../shared/data/sample_posts.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/rate_limit_ui.dart';
import 'categories_provider.dart';
import 'feed_cache.dart';
import 'post_repository.dart';

class FeedState {
  FeedState({
    this.posts = const [],
    this.isLoading = false,
    this.isLoadingMore = false,
    this.hasMore = true,
    this.error,
    this.actionError,
    this.page = 1,
    this.sort = 'trending',
    this.categoryId,
    this.newPostsCount = 0,
  }) : postMap = {for (final p in posts) p.id: p};

  final List<Post> posts;
  final Map<String, Post> postMap;
  final bool isLoading;
  final bool isLoadingMore;
  final bool hasMore;
  final String? error;
  final String? actionError;
  final int page;
  final String sort;
  final int? categoryId;
  final int newPostsCount;

  FeedState copyWith({
    List<Post>? posts,
    bool? isLoading,
    bool? isLoadingMore,
    bool? hasMore,
    String? error,
    String? actionError,
    bool clearError = false,
    bool clearActionError = false,
    int? page,
    String? sort,
    int? categoryId,
    bool clearCategoryId = false,
    int? newPostsCount,
  }) =>
      FeedState(
        posts: posts ?? this.posts,
        isLoading: isLoading ?? this.isLoading,
        isLoadingMore: isLoadingMore ?? this.isLoadingMore,
        hasMore: hasMore ?? this.hasMore,
        error: clearError ? null : (error ?? this.error),
        actionError:
            clearActionError ? null : (actionError ?? this.actionError),
        page: page ?? this.page,
        sort: sort ?? this.sort,
        categoryId: clearCategoryId ? null : (categoryId ?? this.categoryId),
        newPostsCount: newPostsCount ?? this.newPostsCount,
      );
}

class FeedNotifier extends Notifier<FeedState> {
  Timer? _pollTimer;

  @override
  FeedState build() {
    if (!AppRuntime.useRemoteApi) {
      return FeedState(posts: List.of(samplePosts));
    }
    Future.microtask(() => _fetch(page: 1));
    _startPolling();
    ref.onDispose(() => _pollTimer?.cancel());
    return FeedState(isLoading: true);
  }

  void _startPolling() {
    _pollTimer?.cancel();
    _pollTimer = Timer.periodic(
      const Duration(seconds: 60),
      (_) => checkForNewPosts(),
    );
  }

  Future<void> checkForNewPosts() async {
    if (state.isLoading || state.isLoadingMore || state.posts.isEmpty) return;
    if (!AppRuntime.useRemoteApi) return;

    try {
      final latestPosts = await _repo.fetchFeed(
        page: 1,
        limit: 50,
        categoryId: state.categoryId,
        sort: state.sort,
        afterId: state.posts.first.id,
      );

      if (latestPosts.isNotEmpty) {
        state = state.copyWith(newPostsCount: latestPosts.length);
      }
    } catch (_) {}
  }

  PostRepository get _repo => ref.read(postRepositoryProvider);
  FeedCache get _cache => const FeedCache();

  Future<void> load({int? categoryId, String sort = 'trending'}) {
    state = state.copyWith(
      isLoading: true,
      clearError: true,
      categoryId: categoryId,
      clearCategoryId: categoryId == null,
      sort: sort,
      newPostsCount: 0,
    );
    return _fetch(page: 1, categoryId: categoryId, sort: sort);
  }

  Future<void> refresh({bool silent = false}) {
    if (!silent) {
      state =
          state.copyWith(isLoading: true, clearError: true, newPostsCount: 0);
    } else {
      state = state.copyWith(newPostsCount: 0);
    }
    return _fetch(page: 1, categoryId: state.categoryId, sort: state.sort);
  }

  Future<void> loadMore() async {
    if (state.isLoadingMore || !state.hasMore) return;
    state = state.copyWith(isLoadingMore: true);
    await _fetch(page: state.page + 1);
  }

  Future<void> _fetch({
    required int page,
    int? categoryId,
    String? sort,
  }) async {
    final effectiveCategoryId = categoryId ?? state.categoryId;
    final effectiveSort = sort ?? state.sort;

    if (!AppRuntime.useRemoteApi) {
      await Future<void>.delayed(const Duration(milliseconds: 500));
      var results = List.of(samplePosts);
      if (effectiveCategoryId != null && effectiveCategoryId != 0) {
        results =
            results.where((p) => p.category.id == effectiveCategoryId).toList();
      }
      if (effectiveSort == 'new') {
        results.sort((a, b) => b.createdAt.compareTo(a.createdAt));
      } else {
        results.sort((a, b) => b.trendScore.compareTo(a.trendScore));
      }

      state = state.copyWith(
        posts: results,
        isLoading: false,
        isLoadingMore: false,
        page: 1,
        hasMore: false,
        clearError: true,
      );
      return;
    }

    try {
      if (page == 1 && state.posts.isEmpty) {
        final cached = await _cache.read(
          sort: effectiveSort,
          categoryId: effectiveCategoryId,
        );
        if (cached.isNotEmpty) {
          state = state.copyWith(
            posts: cached,
            isLoading: true,
            clearError: true,
          );
        }
      }

      final posts = await _repo.fetchFeed(
        page: page,
        categoryId: effectiveCategoryId,
        sort: effectiveSort,
      );

      // Diversity pass / Impression filtering
      final history = ref.read(historyProvider.notifier);
      final suppressed = ref.read(sessionSuppressedCategoriesProvider);

      final filteredPosts = posts.where((p) {
        if (p.isOwner) return true;
        // Suppress category based on Phase 3 Real-time feedback
        if (suppressed.contains(p.category.id)) return false;

        // Max 3 impressions filter based on docs/recommendation-system.md
        return history.getImpressionCount(p.id) < 3;
      }).toList();

      if (page == 1) {
        await _cache.write(
          sort: effectiveSort,
          categoryId: effectiveCategoryId,
          posts: filteredPosts,
        );
        state = state.copyWith(
          posts: filteredPosts,
          isLoading: false,
          page: 1,
          hasMore: posts.length >= 20,
          clearError: true,
        );
      } else {
        state = state.copyWith(
          posts: [...state.posts, ...filteredPosts],
          isLoadingMore: false,
          page: page,
          hasMore: posts.length >= 20,
        );
      }
    } on ApiException catch (e) {
      state = state.copyWith(
        isLoading: false,
        isLoadingMore: false,
        error: e.friendlyMessage,
      );
    } catch (_) {
      state = state.copyWith(
        isLoading: false,
        isLoadingMore: false,
        error: 'Gönderi listesi yüklenemedi.',
      );
    }
  }

  Future<void> vote(String postId, String voteType) async {
    final original = _findPost(postId);
    if (original == null) return;

    final vt = VoteType.values.byName(voteType);
    _replacePost(_applyVote(original, vt));

    if (!AppRuntime.useRemoteApi) return;

    try {
      final updated = await _repo.vote(postId, vt);
      _replacePost(updated);
    } on ApiException catch (e) {
      _replacePost(original);
      state = state.copyWith(
        actionError: RateLimitUi.messageFor(e, RateLimitedAction.vote),
      );
    } catch (_) {
      _replacePost(original);
      state = state.copyWith(actionError: 'Oy gönderilemedi.');
    }
  }

  Future<void> removeVote(String postId) async {
    final original = _findPost(postId);
    if (original == null) return;

    _replacePost(original.copyWith(clearVote: true));

    if (!AppRuntime.useRemoteApi) return;

    try {
      final updated = await _repo.removeVote(postId);
      _replacePost(updated);
    } on ApiException catch (e) {
      _replacePost(original);
      state = state.copyWith(
        actionError: RateLimitUi.messageFor(e, RateLimitedAction.vote),
      );
    } catch (_) {
      _replacePost(original);
      state = state.copyWith(actionError: 'Oy kaldırılamadı.');
    }
  }

  void clearActionError() {
    state = state.copyWith(clearActionError: true);
  }

  void updatePost(Post post) => _replacePost(post);

  void prependPost(Post post) {
    state = state.copyWith(posts: [post, ...state.posts]);
  }

  void removePost(String postId) {
    state = state.copyWith(
      posts: state.posts.where((post) => post.id != postId).toList(),
    );
  }

  void removePostsByAuthor(String authorId) {
    state = state.copyWith(
      posts: state.posts.where((p) => p.authorId != authorId).toList(),
    );
  }

  Future<Post?> markNotInterested(String postId,
      {String reason = 'not_interested'}) async {
    final post = _findPost(postId);
    if (post == null) return null;
    removePost(postId);

    // Phase 3: Real-time Suppressor
    // Suppress category for this session on strong negative signals.
    if (reason == 'not_interested' || reason == 'seen_too_much') {
      ref
          .read(sessionSuppressedCategoriesProvider.notifier)
          .suppress(post.category.id);
    }

    if (AppRuntime.useRemoteApi) {
      // low_quality is local-only feedback; don't persist to backend.
      if (reason != 'low_quality') {
        try {
          await _repo.markNotInterested(postId, reason: reason);
        } catch (_) {
          // Silently fail — local removal already gives user the right UX
        }
      }
    }
    return post;
  }

  void undoRemove(Post post, int? index) {
    final posts = List.of(state.posts);
    final at = (index ?? posts.length).clamp(0, posts.length);
    posts.insert(at, post);
    state = state.copyWith(posts: posts);
  }

  Post? _findPost(String id) => state.postMap[id];

  void _replacePost(Post post) {
    final index = state.posts.indexWhere((p) => p.id == post.id);
    if (index == -1) return;
    final newPosts = List.of(state.posts)..[index] = post;
    state = state.copyWith(posts: newPosts);
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
}

final feedProvider =
    NotifierProvider<FeedNotifier, FeedState>(FeedNotifier.new);

// Tracks keyboard-focused post index for j/k/Enter desktop navigation (web only)
final feedFocusIndexProvider = StateProvider<int?>((ref) => null);
