import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/models/post.dart';

final trendTopicsProvider = FutureProvider.autoDispose<List<TrendTopic>>((ref) async {
  if (!AppRuntime.useRemoteApi) {
    return const [
      TrendTopic(name: 'HaftaSonu', postCount: 482, growthPercent: 34),
      TrendTopic(name: 'Patronum', postCount: 317, growthPercent: 12),
      TrendTopic(name: 'Düğün', postCount: 298, growthPercent: 8),
      TrendTopic(name: 'Arkadaşlar', postCount: 201),
      TrendTopic(name: 'Kira', postCount: 189),
    ];
  }
  return ref.watch(postRepositoryProvider).fetchTrendTopics();
});

final discoverProvider = FutureProvider.autoDispose<DiscoverData>((ref) async {
  if (!AppRuntime.useRemoteApi) {
    return const DiscoverData(
      rising: [],
      controversial: [],
      fresh: [],
      trendTopics: [
        TrendTopic(name: 'HaftaSonu', postCount: 482, growthPercent: 34),
        TrendTopic(name: 'Patronum', postCount: 317, growthPercent: 12),
        TrendTopic(name: 'Düğün', postCount: 298, growthPercent: 8),
        TrendTopic(name: 'Arkadaşlar', postCount: 201),
        TrendTopic(name: 'Kira', postCount: 189),
      ],
      todaysPosts: [],
    );
  }

  final repo = ref.watch(postRepositoryProvider);
  final discover = await repo.fetchDiscover();
  final todays = await repo.fetchTodaysPosts().catchError((_) => <Post>[]);
  return DiscoverData(
    rising: discover.rising,
    controversial: discover.controversial,
    fresh: discover.fresh,
    cityTrending: discover.cityTrending,
    city: discover.city,
    trendTopics: discover.trendTopics,
    todaysPosts: todays,
  );
});
