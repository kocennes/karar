import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_services.dart';
import '../../core/providers.dart';

class UserSearchResult {
  const UserSearchResult({
    required this.username,
    required this.karma,
    required this.postCount,
  });

  final String username;
  final int karma;
  final int postCount;
}

class UserSearchState {
  const UserSearchState({
    this.results = const [],
    this.isLoading = false,
    this.error,
    this.query = '',
  });

  final List<UserSearchResult> results;
  final bool isLoading;
  final String? error;
  final String query;

  UserSearchState copyWith({
    List<UserSearchResult>? results,
    bool? isLoading,
    String? error,
    bool clearError = false,
    String? query,
  }) =>
      UserSearchState(
        results: results ?? this.results,
        isLoading: isLoading ?? this.isLoading,
        error: clearError ? null : (error ?? this.error),
        query: query ?? this.query,
      );
}

class UserSearchNotifier extends Notifier<UserSearchState> {
  @override
  UserSearchState build() => const UserSearchState();

  void search(String query) {
    if (query.length < 3) {
      state = state.copyWith(results: [], query: query, clearError: true);
      return;
    }

    state = state.copyWith(isLoading: true, query: query, clearError: true);
    _fetch(query);
  }

  void clear() {
    state = const UserSearchState();
  }

  Future<void> _fetch(String query) async {
    if (!AppRuntime.useRemoteApi) {
      final mock = [
        UserSearchResult(username: query, karma: 247, postCount: 12),
        UserSearchResult(username: '${query}m', karma: 8, postCount: 3),
        UserSearchResult(username: '${query}_user', karma: 1203, postCount: 38),
      ];
      if (state.query == query) {
        state = state.copyWith(results: mock, isLoading: false);
      }
      return;
    }

    try {
      final repo = ref.read(postRepositoryProvider);
      final raw = await repo.searchUsers(query);
      final results = raw
          .map((u) => UserSearchResult(
                username: u['username'] as String,
                karma: u['karma'] as int? ?? 0,
                postCount: u['postCount'] as int? ?? 0,
              ))
          .toList();
      if (state.query == query) {
        state = state.copyWith(results: results, isLoading: false);
      }
    } catch (e) {
      if (state.query == query) {
        state = state.copyWith(
          isLoading: false,
          error: 'Kullanıcılar yüklenemedi.',
        );
      }
    }
  }
}

final userSearchProvider =
    NotifierProvider<UserSearchNotifier, UserSearchState>(
  UserSearchNotifier.new,
);
