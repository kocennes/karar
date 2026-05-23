import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/data/sample_posts.dart';
import '../../shared/models/post.dart';

final weeklyFeaturedProvider = FutureProvider.autoDispose<Post?>((ref) async {
  if (!AppRuntime.useRemoteApi) {
    // Dev mode: return the highest-voted sample post
    final sorted = [...samplePosts]
      ..sort((a, b) => b.totalVotes.compareTo(a.totalVotes));
    return sorted.isNotEmpty ? sorted.first : null;
  }

  return ref.watch(postRepositoryProvider).fetchWeeklyFeatured();
});
