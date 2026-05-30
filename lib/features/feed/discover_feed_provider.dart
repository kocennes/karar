import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/models/post.dart';

class DiscoverFeedNotifier extends AsyncNotifier<DiscoverFeedState> {
  @override
  Future<DiscoverFeedState> build() async {
    if (!AppRuntime.useRemoteApi) {
      return const DiscoverFeedState();
    }
    return ref.read(postRepositoryProvider).fetchDiscoverFeed();
  }

  Future<void> loadMore() async {
    final current = state.valueOrNull;
    if (current == null || current.isLoadingMore || !current.hasMore) return;
    state = AsyncData(current.copyWith(isLoadingMore: true));
    try {
      final more = await ref.read(postRepositoryProvider).fetchDiscoverFeed(
            cursor: current.nextCursor,
          );
      state = AsyncData(DiscoverFeedState(
        items: [...current.items, ...more.items],
        nextCursor: more.nextCursor,
      ));
    } catch (_) {
      state = AsyncData(current.copyWith(isLoadingMore: false));
    }
  }

  Future<void> vote(String postId, VoteType voteType,
      {String? impressionToken}) async {
    final current = state.valueOrNull;
    if (current == null) return;
    final item = current.items.firstWhere((i) => i.post.id == postId);
    final original = item.post;
    try {
      final repo = ref.read(postRepositoryProvider);
      final Post updated;
      if (original.myVote == voteType) {
        updated = await repo.removeVote(postId);
      } else {
        updated = await repo.vote(postId, voteType);
        repo.sendDiscoverEvent(
          postId: postId,
          eventType: 'vote',
          impressionToken: impressionToken ?? item.impressionToken,
          rankingReason: item.rankingReason,
        );
        final position = current.items.indexWhere((i) => i.post.id == postId);
        ref.read(analyticsServiceProvider).logDiscoverVote(
              postId: postId,
              voteType: voteType.name,
              position: position < 0 ? 0 : position,
              rankingReason: item.rankingReason,
            );
        ref.read(analyticsServiceProvider).logVerdictViewed(
              postId: postId,
              voteType: voteType.name,
              source: 'discover',
              rankingReason: item.rankingReason,
            );
      }
      _updatePost(updated);
    } catch (_) {}
  }

  void _updatePost(Post post) {
    final current = state.valueOrNull;
    if (current == null) return;
    state = AsyncData(current.copyWith(
      items: current.items.map((item) {
        return item.post.id == post.id ? item.copyWith(post: post) : item;
      }).toList(),
    ));
  }

  void removeItem(String postId) {
    final current = state.valueOrNull;
    if (current == null) return;
    state = AsyncData(current.copyWith(
      items: current.items.where((i) => i.post.id != postId).toList(),
    ));
  }
}

final discoverFeedProvider =
    AsyncNotifierProvider<DiscoverFeedNotifier, DiscoverFeedState>(
  DiscoverFeedNotifier.new,
);
