import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/models/post.dart';

class MyPostsState {
  const MyPostsState({
    this.posts = const [],
    this.isLoading = false,
    this.error,
    this.sort = 'new',
  });

  final List<Post> posts;
  final bool isLoading;
  final String? error;
  final String sort;

  MyPostsState copyWith({
    List<Post>? posts,
    bool? isLoading,
    String? error,
    String? sort,
  }) =>
      MyPostsState(
        posts: posts ?? this.posts,
        isLoading: isLoading ?? this.isLoading,
        error: error,
        sort: sort ?? this.sort,
      );
}

class MyPostsNotifier extends Notifier<MyPostsState> {
  @override
  MyPostsState build() {
    Future.microtask(load);
    return const MyPostsState(isLoading: true);
  }

  Future<void> load() async {
    if (!AppRuntime.useRemoteApi) {
      state = const MyPostsState(posts: []);
      return;
    }

    state = state.copyWith(isLoading: true, error: null);
    try {
      final posts = await ref
          .read(postRepositoryProvider)
          .fetchMyPosts(sort: state.sort);
      state = state.copyWith(posts: posts, isLoading: false);
    } on ApiException catch (e) {
      state = state.copyWith(isLoading: false, error: e.friendlyMessage);
    } catch (_) {
      state = state.copyWith(isLoading: false, error: 'Gönderilerin yüklenemedi.');
    }
  }

  Future<void> setSort(String sort) async {
    state = state.copyWith(sort: sort);
    await load();
  }
}

final myPostsProvider = NotifierProvider<MyPostsNotifier, MyPostsState>(
  MyPostsNotifier.new,
);
