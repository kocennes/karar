import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/data/sample_posts.dart' as samples;
import '../../shared/models/post.dart';
import 'feed_provider.dart';

final categoriesProvider = FutureProvider<List<Category>>((ref) async {
  if (!AppRuntime.useRemoteApi) {
    return samples.categories;
  }

  final repo = ref.watch(postRepositoryProvider);
  return repo.fetchCategories();
});

class FollowedCategoriesNotifier extends Notifier<Set<int>> {
  @override
  Set<int> build() {
    _load();
    return {};
  }

  static const _kKey = 'followed_categories';

  Future<void> _load() async {
    final prefs = await SharedPreferences.getInstance();
    state = (prefs.getStringList(_kKey) ?? []).map(int.parse).toSet();
  }

  Future<void> toggle(int id) async {
    final newState = Set<int>.from(state);
    final isFollowing = newState.contains(id);

    if (isFollowing) {
      newState.remove(id);
    } else {
      newState.add(id);
    }
    state = newState;

    final prefs = await SharedPreferences.getInstance();
    await prefs.setStringList(
        _kKey, newState.map((e) => e.toString()).toList());

    if (AppRuntime.useRemoteApi && ref.read(currentUserProvider) != null) {
      try {
        if (isFollowing) {
          await ref.read(postRepositoryProvider).unfollowCategory(id);
        } else {
          await ref.read(postRepositoryProvider).followCategory(id);
        }
      } catch (_) {}
    }
  }
}

final followedCategoriesProvider =
    NotifierProvider<FollowedCategoriesNotifier, Set<int>>(
  FollowedCategoriesNotifier.new,
);

class MutedCategoriesNotifier extends Notifier<Set<int>> {
  @override
  Set<int> build() {
    _load();
    return {};
  }

  static const _kKey = 'muted_categories';

  Future<void> _load() async {
    final prefs = await SharedPreferences.getInstance();
    state = (prefs.getStringList(_kKey) ?? []).map(int.parse).toSet();
  }

  Future<void> toggle(int id) async {
    final newState = Set<int>.from(state);
    final isMuted = newState.contains(id);

    if (isMuted) {
      newState.remove(id);
    } else {
      newState.add(id);
      // Sessize alınan kategoriyi takipten de çıkaralım
      if (ref.read(followedCategoriesProvider).contains(id)) {
        await ref.read(followedCategoriesProvider.notifier).toggle(id);
      }
    }
    state = newState;

    final prefs = await SharedPreferences.getInstance();
    await prefs.setStringList(
        _kKey, newState.map((e) => e.toString()).toList());

    if (AppRuntime.useRemoteApi && ref.read(currentUserProvider) != null) {
      try {
        if (isMuted) {
          await ref.read(postRepositoryProvider).unmuteCategory(id);
        } else {
          await ref.read(postRepositoryProvider).muteCategory(id);
        }
      } catch (_) {}
    }

    // Feed'i yenile ki sessize alınanlar gitsin
    ref.read(feedProvider.notifier).refresh();
  }
}

final mutedCategoriesProvider =
    NotifierProvider<MutedCategoriesNotifier, Set<int>>(
  MutedCategoriesNotifier.new,
);

class SessionSuppressedCategoriesNotifier extends Notifier<Set<int>> {
  @override
  Set<int> build() => {};

  void suppress(int id) {
    state = {...state, id};
    // Trigger feed refresh or filter locally
    ref.read(feedProvider.notifier).refresh(silent: true);
  }

  void clear() => state = {};
}

final sessionSuppressedCategoriesProvider =
    NotifierProvider<SessionSuppressedCategoriesNotifier, Set<int>>(
  SessionSuppressedCategoriesNotifier.new,
);
