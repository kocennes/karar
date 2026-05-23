import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/data/sample_posts.dart';
import '../../shared/models/post.dart';

class SearchState {
  const SearchState({
    this.results = const [],
    this.isLoading = false,
    this.error,
    this.query = '',
    this.categoryId,
    this.minVotes,
    this.dateRange,
    this.sort = 'relevance',
  });

  final List<Post> results;
  final bool isLoading;
  final String? error;
  final String query;
  final int? categoryId;
  final int? minVotes;
  final DateTimeRange? dateRange;
  final String sort;

  SearchState copyWith({
    List<Post>? results,
    bool? isLoading,
    String? error,
    String? query,
    int? categoryId,
    bool clearCategoryId = false,
    int? minVotes,
    bool clearMinVotes = false,
    DateTimeRange? dateRange,
    bool clearDateRange = false,
    String? sort,
  }) =>
      SearchState(
        results: results ?? this.results,
        isLoading: isLoading ?? this.isLoading,
        error: error,
        query: query ?? this.query,
        categoryId: clearCategoryId ? null : (categoryId ?? this.categoryId),
        minVotes: clearMinVotes ? null : (minVotes ?? this.minVotes),
        dateRange: clearDateRange ? null : (dateRange ?? this.dateRange),
        sort: sort ?? this.sort,
      );
}

class SearchNotifier extends Notifier<SearchState> {
  @override
  SearchState build() => const SearchState(sort: 'relevance');

  Future<void> search(String query) async {
    state = state.copyWith(query: query);
    _performSearch();
  }

  void setSort(String sort) {
    state = state.copyWith(sort: sort);
    _performSearch();
  }

  void setCategory(int? id) {
    state = state.copyWith(categoryId: id, clearCategoryId: id == null);
    _performSearch();
  }

  void setMinVotes(int? votes) {
    state = state.copyWith(minVotes: votes, clearMinVotes: votes == null);
    _performSearch();
  }

  void setDateRange(DateTimeRange? range) {
    state = state.copyWith(dateRange: range, clearDateRange: range == null);
    _performSearch();
  }

  Future<void> _performSearch() async {
    final query = state.query;
    if (query.length < 3) {
      state = state.copyWith(results: [], isLoading: false);
      return;
    }

    if (!AppRuntime.useRemoteApi) {
      // Mock search with filters
      var results = samplePosts.where((p) =>
          p.title.toLowerCase().contains(query.toLowerCase()) ||
          p.content.toLowerCase().contains(query.toLowerCase())).toList();

      if (state.categoryId != null) {
        results = results.where((p) => p.category.id == state.categoryId).toList();
      }
      if (state.minVotes != null) {
        results = results.where((p) => p.totalVotes >= state.minVotes!).toList();
      }
      if (state.dateRange != null) {
        results = results.where((p) =>
            p.createdAt.isAfter(state.dateRange!.start) &&
            p.createdAt.isBefore(state.dateRange!.end.add(const Duration(days: 1)))).toList();
      }

      state = state.copyWith(results: results, isLoading: false);
      return;
    }

    state = state.copyWith(isLoading: true, error: null);
    ref.read(analyticsServiceProvider).logSearchPerformed(query: query);

    try {
      final results = await ref.read(postRepositoryProvider).search(
            query,
            categoryId: state.categoryId,
            minVotes: state.minVotes,
            startDate: state.dateRange?.start,
            endDate: state.dateRange?.end,
            sort: state.sort,
          );
      state = state.copyWith(results: results, isLoading: false);
    } catch (e) {
      state = state.copyWith(
        results: [],
        isLoading: false,
        error: 'Arama sırasında bir hata oluştu.',
      );
    }
  }

  void clear() {
    state = const SearchState(sort: 'relevance');
  }
}

final searchProvider = NotifierProvider<SearchNotifier, SearchState>(
  SearchNotifier.new,
);
