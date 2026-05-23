import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/data/sample_posts.dart';
import 'feed_provider.dart';
import 'post_repository.dart';

class CategoryFeedNotifier extends FamilyNotifier<FeedState, int> {
  @override
  FeedState build(int arg) {
    if (!AppRuntime.useRemoteApi) {
      final filtered = samplePosts.where((p) => p.category.id == arg).toList();
      return FeedState(posts: filtered, categoryId: arg);
    }
    Future.microtask(() => _fetch(page: 1));
    return FeedState(isLoading: true, categoryId: arg);
  }

  PostRepository get _repo => ref.read(postRepositoryProvider);

  Future<void> refresh() {
    state = state.copyWith(isLoading: true, clearError: true);
    return _fetch(page: 1);
  }

  Future<void> loadMore() async {
    if (state.isLoadingMore || !state.hasMore) return;
    state = state.copyWith(isLoadingMore: true);
    await _fetch(page: state.page + 1);
  }

  Future<void> changeSort(String sort) {
    state = state.copyWith(isLoading: true, sort: sort, clearError: true);
    return _fetch(page: 1, sort: sort);
  }

  Future<void> _fetch({required int page, String? sort}) async {
    final effectiveSort = sort ?? state.sort;
    final categoryId = arg;

    if (!AppRuntime.useRemoteApi) {
      await Future<void>.delayed(const Duration(milliseconds: 400));
      var results =
          samplePosts.where((p) => p.category.id == categoryId).toList();
      if (effectiveSort == 'new') {
        results.sort((a, b) => b.createdAt.compareTo(a.createdAt));
      } else {
        results.sort((a, b) => b.trendScore.compareTo(a.trendScore));
      }
      state = FeedState(
        posts: results,
        isLoading: false,
        sort: effectiveSort,
        categoryId: categoryId,
        hasMore: false,
      );
      return;
    }

    try {
      final result = await _repo.fetchFeed(
        page: page,
        categoryId: categoryId,
        sort: effectiveSort,
      );

      final newPosts = page == 1 ? result : [...state.posts, ...result];
      state = state.copyWith(
        posts: newPosts,
        isLoading: false,
        isLoadingMore: false,
        sort: effectiveSort,
        page: page,
        hasMore: result.length >= 20,
        clearError: true,
      );
    } catch (e) {
      state = state.copyWith(
        isLoading: false,
        isLoadingMore: false,
        error: e.toString(),
      );
    }
  }
}

final categoryFeedProvider =
    NotifierProvider.family<CategoryFeedNotifier, FeedState, int>(
  CategoryFeedNotifier.new,
);
