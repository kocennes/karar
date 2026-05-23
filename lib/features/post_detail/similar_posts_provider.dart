import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/data/sample_posts.dart';
import '../../shared/models/post.dart';

final similarPostsProvider =
    FutureProvider.autoDispose.family<List<Post>, String>((ref, postId) async {
  if (!AppRuntime.useRemoteApi) {
    await Future<void>.delayed(const Duration(milliseconds: 600));
    final all = samplePosts;
    return all.where((p) => p.id != postId).take(4).toList();
  }

  final repo = ref.read(postRepositoryProvider);
  return repo.fetchSimilarPosts(postId);
});
