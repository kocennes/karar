import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/models/post.dart';

final postStatsProvider =
    FutureProvider.autoDispose.family<PostStats, String>((ref, postId) async {
  if (!AppRuntime.useRemoteApi) {
    await Future<void>.delayed(const Duration(milliseconds: 500));
    return const PostStats(
      viewCount: 1847,
      voteRate: 34,
      avgReadingSeconds: 28,
      voteTimeline: [2, 5, 18, 42, 31, 19, 11, 7, 5, 3, 2, 1],
    );
  }

  final repo = ref.read(postRepositoryProvider);
  return repo.fetchPostStats(postId);
});
