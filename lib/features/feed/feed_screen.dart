import 'package:firebase_crashlytics/firebase_crashlytics.dart';
import 'package:flutter/foundation.dart' hide Category;
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/layout/breakpoints.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/login_nudge.dart';
import '../../shared/widgets/skeleton.dart';
import '../../core/history/history_provider.dart';
import '../../shared/widgets/karar_logo.dart';
import '../../core/network/connectivity_provider.dart';
import '../../core/theme/app_colors.dart';
import '../ads/banner_ad_widget.dart';
import 'categories_provider.dart';
import 'feed_provider.dart';
import 'post_card.dart';
import 'trend_topics_panel.dart';
import 'weekly_featured_card.dart';

class FeedScreen extends ConsumerStatefulWidget {
  const FeedScreen({super.key});

  @override
  ConsumerState<FeedScreen> createState() => _FeedScreenState();
}

class _FeedScreenState extends ConsumerState<FeedScreen> {
  var _selectedCategoryId = 0;
  var _sortMode = _FeedSort.trending;
  var _showFab = false;
  var _showTopBar = true;
  String? _lastRouteState;
  final _scrollController = ScrollController();
  ProviderSubscription<AsyncValue<ConnectivityStatus>>? _connectivitySub;
  final _itemKeys = <int, GlobalKey>{};

  static const _fabThreshold = 400.0;

  @override
  void initState() {
    super.initState();
    _scrollController.addListener(_onScroll);

    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!context.mounted) return;

      _checkCrash();

      final extra = GoRouterState.of(context).extra;
      if (extra is Map<String, dynamic> && extra.containsKey('categoryId')) {
        _onCategoryChanged(extra['categoryId'] as int);
      }
    });

    _connectivitySub = ref.listenManual(connectivityProvider, (previous, next) {
      if (next.value == ConnectivityStatus.isConnected &&
          previous?.value == ConnectivityStatus.isDisconnected) {
        ref.read(feedProvider.notifier).refresh();
      }
    });
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _syncStateFromRoute();
  }

  void _scrollToFocusedIndex(int index) {
    final key = _itemKeys[index];
    if (key?.currentContext != null) {
      Scrollable.ensureVisible(
        key!.currentContext!,
        alignment: 0.3,
        duration: const Duration(milliseconds: 300),
        curve: Curves.easeOut,
      );
    } else if (_scrollController.hasClients) {
      const estimatedItemHeight = 200.0;
      const separatorHeight = 10.0;
      final offset = index * (estimatedItemHeight + separatorHeight);
      _scrollController.animateTo(
        offset.clamp(0.0, _scrollController.position.maxScrollExtent),
        duration: const Duration(milliseconds: 300),
        curve: Curves.easeOut,
      );
    }
  }

  @override
  void dispose() {
    _connectivitySub?.close();
    _scrollController.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (_scrollController.position.pixels >=
        _scrollController.position.maxScrollExtent - 300) {
      ref.read(feedProvider.notifier).loadMore();
    }
    
    // FAB visibility
    final shouldShowFab = _scrollController.position.pixels > _fabThreshold;
    if (shouldShowFab != _showFab) setState(() => _showFab = shouldShowFab);

    // Top bar visibility (hide on scroll down, show on scroll up)
    if (_scrollController.position.userScrollDirection == ScrollDirection.reverse) {
      if (_showTopBar) setState(() => _showTopBar = false);
    } else if (_scrollController.position.userScrollDirection == ScrollDirection.forward) {
      if (!_showTopBar) setState(() => _showTopBar = true);
    }
  }

  Future<void> _checkCrash() async {
    try {
      if (kIsWeb) return;
      final crashed =
          await FirebaseCrashlytics.instance.didCrashOnPreviousExecution();
      if (crashed && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
                'Uygulama beklenmedik şekilde kapandı. Geri bildirim için teşekkürler.'),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    } catch (_) {}
  }

  Future<void> _onRefresh() async {
    if (_isDisconnected) {
      _showNoConnectionSnack();
      return;
    }
    await ref.read(feedProvider.notifier).refresh();
  }

  void _syncStateFromRoute() {
    final uri = GoRouterState.of(context).uri;
    final routeState = uri.queryParameters.toString();
    if (_lastRouteState == routeState) return;
    _lastRouteState = routeState;

    final sort = _FeedSort.fromQuery(uri.queryParameters['sort']);
    final categoryId = int.tryParse(uri.queryParameters['category'] ?? '') ?? 0;
    if (sort == _sortMode && categoryId == _selectedCategoryId) return;

    setState(() {
      _sortMode = sort;
      _selectedCategoryId = categoryId;
    });
    ref.read(feedProvider.notifier).load(
          categoryId: categoryId == 0 ? null : categoryId,
          sort: sort.queryValue,
        );
  }

  void _replaceRouteState({
    _FeedSort? sort,
    int? categoryId,
  }) {
    final nextSort = sort ?? _sortMode;
    final nextCategoryId = categoryId ?? _selectedCategoryId;
    final params = <String, String>{};
    if (nextSort != _FeedSort.trending) {
      params['sort'] = nextSort.queryValue;
    }
    if (nextCategoryId != 0) {
      params['category'] = nextCategoryId.toString();
    }
    final uri = Uri(path: '/', queryParameters: params.isEmpty ? null : params);
    final nextLocation = uri.toString();
    _lastRouteState = params.toString();
    if (GoRouterState.of(context).uri.toString() != nextLocation) {
      context.go(nextLocation);
    }
  }

  void _onCategoryChanged(int id) {
    if (_isDisconnected) {
      _showNoConnectionSnack();
      return;
    }
    setState(() => _selectedCategoryId = id);
    _replaceRouteState(categoryId: id);
    final categoryId = id == 0 ? null : id;
    ref.read(feedProvider.notifier).load(
          categoryId: categoryId,
          sort: _sortMode.queryValue,
        );
  }

  void _onSortChanged(_FeedSort sort) {
    if (_isDisconnected) {
      _showNoConnectionSnack();
      return;
    }
    setState(() => _sortMode = sort);
    _replaceRouteState(sort: sort);
    ref.read(feedProvider.notifier).load(
          categoryId: _selectedCategoryId == 0 ? null : _selectedCategoryId,
          sort: sort.queryValue,
        );
  }

  void _showCategoryOptions(Category category) {
    if (category.id == 0) return;

    final isFollowed =
        ref.read(followedCategoriesProvider).contains(category.id);
    final isMuted = ref.read(mutedCategoriesProvider).contains(category.id);

    showModalBottomSheet(
      context: context,
      builder: (context) => Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          ListTile(
            leading: Icon(isFollowed ? Icons.star : Icons.star_border),
            title: Text(isFollowed ? 'Takibi Bırak' : 'Takip Et'),
            onTap: () {
              Navigator.pop(context);
              _toggleFollow(category);
            },
          ),
          ListTile(
            leading: Icon(isMuted ? Icons.volume_up : Icons.volume_off),
            title: Text(isMuted ? 'Sessizden Çıkar' : 'Sessize Al'),
            subtitle: isMuted
                ? null
                : const Text('Bu kategorideki gönderileri akışında görmezsin.'),
            onTap: () {
              Navigator.pop(context);
              _toggleMute(category);
            },
          ),
          const SizedBox(height: 20),
        ],
      ),
    );
  }

  void _toggleMute(Category category) {
    if (_isDisconnected) {
      _showNoConnectionSnack();
      return;
    }
    ref.read(mutedCategoriesProvider.notifier).toggle(category.id);
    final isMuted = ref.read(mutedCategoriesProvider).contains(category.id);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          isMuted
              ? '${category.name} sessize alındı.'
              : '${category.name} artık akışında görünecek.',
        ),
        duration: const Duration(seconds: 1),
      ),
    );
  }

  void _toggleFollow(Category category) {
    if (_isDisconnected) {
      _showNoConnectionSnack();
      return;
    }
    if (ref.read(currentUserProvider) == null) {
      LoginNudge.show(
        context,
        title: 'Kategoriyi Takip Et',
        message:
            'Takip ettiğin kategorilerdeki yeni paylaşımlardan haberdar olmak için giriş yapmalısın.',
      );
      return;
    }
    ref.read(followedCategoriesProvider.notifier).toggle(category.id);
    final isFollowed =
        ref.read(followedCategoriesProvider).contains(category.id);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          isFollowed
              ? '${category.name} takip ediliyor.'
              : '${category.name} takibi bırakıldı.',
        ),
        duration: const Duration(seconds: 1),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(feedProvider, (previous, next) {
      if (next.actionError != null &&
          previous?.actionError != next.actionError) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(next.actionError!),
            backgroundColor: Theme.of(context).colorScheme.error,
            behavior: SnackBarBehavior.floating,
          ),
        );
        ref.read(feedProvider.notifier).clearActionError();
      }
    });

    // j/k klavye navigasyonu: odaklanan karta kaydır
    ref.listen(feedFocusIndexProvider, (previous, next) {
      if (next != null && next != previous) {
        _scrollToFocusedIndex(next);
      }
    });

    final feedState = ref.watch(feedProvider);
    final focusedIndex = ref.watch(feedFocusIndexProvider);
    final filtered = feedState.posts;

    final categoriesAsync = ref.watch(categoriesProvider);
    final categories = categoriesAsync.valueOrNull ?? [];
    final connectivity = ref.watch(connectivityProvider);
    final isDisconnected =
        connectivity.value == ConnectivityStatus.isDisconnected;

    return Scaffold(
      appBar: AppBar(
        leadingWidth: 180,
        leading: InkWell(
          onTap: () {
            if (GoRouterState.of(context).uri.toString() == '/') {
              _scrollToTop();
            } else {
              context.go('/');
            }
          },
          child: const Padding(
            padding: EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: KararLogo(size: LogoSize.medium),
          ),
        ),
        actions: [
          IconButton(
            tooltip: 'Keşfet',
            onPressed: () => context.push('/discover'),
            icon: const Icon(Icons.explore_outlined),
          ),
          IconButton(
            tooltip: 'Ara',
            onPressed: () => context.push('/search'),
            icon: const Icon(Icons.search),
          ),
        ],
      ),
      floatingActionButton: AnimatedSlide(
        duration: const Duration(milliseconds: 250),
        offset: _showFab ? Offset.zero : const Offset(0, 2),
        child: AnimatedOpacity(
          duration: const Duration(milliseconds: 250),
          opacity: _showFab ? 1 : 0,
          child: FloatingActionButton.small(
            tooltip: 'Başa dön',
            onPressed: _scrollToTop,
            child: const Icon(Icons.keyboard_arrow_up),
          ),
        ),
      ),
      body: Column(
        children: [
          if (isDisconnected) _buildDisconnectedBanner(),
          const BannerAdWidget(),
          AnimatedSize(
            duration: const Duration(milliseconds: 300),
            curve: Curves.easeInOut,
            child: _showTopBar 
              ? Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    if (!context.isDesktop) const WeeklyFeaturedCard(),
                    Padding(
                      padding: const EdgeInsets.fromLTRB(16, 10, 16, 0),
                      child: SegmentedButton<_FeedSort>(
                        segments: const [
                          ButtonSegment(
                            value: _FeedSort.trending,
                            label: Text('Trend'),
                            icon: Icon(Icons.local_fire_department_outlined),
                          ),
                          ButtonSegment(
                            value: _FeedSort.newest,
                            label: Text('Yeni'),
                            icon: Icon(Icons.schedule_outlined),
                          ),
                        ],
                        selected: {_sortMode},
                        onSelectionChanged: (s) => _onSortChanged(s.first),
                      ),
                    ),
                    if (categories.isNotEmpty)
                      SizedBox(
                        height: 52,
                        child: ListView.separated(
                          padding:
                              const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                          scrollDirection: Axis.horizontal,
                          itemCount: categories.length,
                          separatorBuilder: (_, __) => const SizedBox(width: 8),
                          itemBuilder: (context, index) {
                            final category = categories[index];
                            final isFollowed = ref
                                .watch(followedCategoriesProvider)
                                .contains(category.id);

                            return GestureDetector(
                              onLongPress: () => _showCategoryOptions(category),
                              child: ChoiceChip(
                                label: Row(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    Text(category.name),
                                    if (isFollowed && category.id != 0) ...[
                                      const SizedBox(width: 4),
                                      const Icon(Icons.star, size: 12),
                                    ],
                                  ],
                                ),
                                selected: _selectedCategoryId == category.id,
                                onSelected: (_) => _onCategoryChanged(category.id),
                              ),
                            );
                          },
                        ),
                      ),
                  ],
                )
              : const SizedBox.shrink(),
          ),
          AnimatedSwitcher(
            duration: const Duration(milliseconds: 220),
            transitionBuilder: (child, animation) => SizeTransition(
              sizeFactor: animation,
              child: FadeTransition(opacity: animation, child: child),
            ),
            child: feedState.newPostsCount > 0
                ? Padding(
                    key: ValueKey(feedState.newPostsCount),
                    padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                    child: _NewPostsBanner(
                      count: feedState.newPostsCount,
                      onTap: () async {
                        await _scrollToTop();
                        await ref.read(feedProvider.notifier).refresh();
                      },
                    ),
                  )
                : const SizedBox.shrink(),
          ),
          Expanded(
            child: context.isDesktop
                ? _buildDesktopLayout(feedState, filtered, focusedIndex)
                : CenteredContent(
                    child: _buildBody(feedState, filtered, focusedIndex)),
          ),
        ],
      ),
    );
  }

  Widget _buildDesktopLayout(
      FeedState feedState, List<Post> posts, int? focusedIndex) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: CenteredContent(
              child: _buildBody(feedState, posts, focusedIndex)),
        ),
        const VerticalDivider(width: 1),
        SizedBox(width: 300, child: _buildRightPanel()),
      ],
    );
  }

  Widget _buildRightPanel() {
    return const SingleChildScrollView(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(height: 16),
          WeeklyFeaturedCard(),
          SizedBox(height: 8),
          TrendTopicsPanel(),
        ],
      ),
    );
  }

  Future<void> _scrollToTop() async {
    if (!_scrollController.hasClients) return;
    await _scrollController.animateTo(
      0,
      duration: const Duration(milliseconds: 400),
      curve: Curves.easeOutCubic,
    );
  }

  Widget _buildDisconnectedBanner() {
    final colorScheme = Theme.of(context).colorScheme;
    return Container(
      width: double.infinity,
      color: Colors.amber.shade100,
      padding: const EdgeInsets.symmetric(vertical: 8, horizontal: 16),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(Icons.signal_wifi_connected_no_internet_4_outlined,
              size: 16, color: Colors.amber.shade900),
          const SizedBox(width: 8),
          Text(
            'İnternet bağlantısı yok',
            style: TextStyle(
              color: colorScheme.onSurface,
              fontSize: 12,
              fontWeight: FontWeight.bold,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildOfflineFallback() {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.wifi_off_rounded,
              size: 56,
              color: Theme.of(context).colorScheme.outline,
            ),
            const SizedBox(height: 20),
            Text(
              'İnternet bağlantısı yok',
              style: Theme.of(context)
                  .textTheme
                  .titleLarge
                  ?.copyWith(fontWeight: FontWeight.w800),
            ),
            const SizedBox(height: 8),
            Text(
              'Bağlantı gelince akış otomatik yenilenecek.',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    color: AppColors.textSecondary,
                    height: 1.5,
                  ),
            ),
            const SizedBox(height: 24),
            OutlinedButton.icon(
              onPressed: _onRefresh,
              icon: const Icon(Icons.refresh),
              label: const Text('Tekrar Dene'),
            ),
          ],
        ),
      ),
    );
  }

  bool get _isDisconnected =>
      ref.read(connectivityProvider).value == ConnectivityStatus.isDisconnected;

  void _showNoConnectionSnack() {
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text('Bağlantı yok. İnternete bağlanınca akış yenilenecek.'),
        behavior: SnackBarBehavior.floating,
      ),
    );
  }

  Widget _buildBody(FeedState feedState, List<Post> posts, int? focusedIndex) {
    if (feedState.isLoading && posts.isEmpty) {
      return ListView.separated(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
        itemCount: 5,
        separatorBuilder: (_, __) => const SizedBox(height: 10),
        itemBuilder: (_, __) => const PostCardSkeleton(),
      );
    }

    if (feedState.error != null && posts.isEmpty) {
      if (_isDisconnected) return _buildOfflineFallback();
      return ErrorView(
        message: feedState.error!,
        onRetry: _onRefresh,
      );
    }

    if (posts.isEmpty) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Text('⚖️', style: TextStyle(fontSize: 56)),
              const SizedBox(height: 20),
              Text(
                'Henüz paylaşım yok',
                style: Theme.of(context).textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
              ),
              const SizedBox(height: 10),
              Text(
                'Bir durum anlat, topluluk Haklı mı Haksız mı karar versin.',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                      color: AppColors.textSecondary,
                      height: 1.5,
                    ),
              ),
              const SizedBox(height: 24),
              FilledButton.icon(
                onPressed: () => context.go('/create'),
                icon: const Icon(Icons.add),
                label: const Text('Paylaşım Yap'),
              ),
            ],
          ),
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: _onRefresh,
      child: ListView.separated(
        controller: _scrollController,
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
        itemCount: posts.length + (feedState.isLoadingMore ? 3 : 0),
        separatorBuilder: (_, __) => const SizedBox(height: 10),
        itemBuilder: (context, index) {
          if (index >= posts.length) {
            return const PostCardSkeleton();
          }
          final post = posts[index];
          final seenPosts = ref.watch(historyProvider);
          final itemKey = _itemKeys.putIfAbsent(index, () => GlobalKey());
          return KeyedSubtree(
            key: itemKey,
            child: PostCard(
              post: post,
              isSeen: seenPosts.contains(post.id),
              isFocused: focusedIndex == index,
              onTap: () => context.push('/posts/${post.id}', extra: post),
            ),
          );
        },
      ),
    );
  }
}

enum _FeedSort {
  trending,
  newest;

  String get queryValue => switch (this) {
        _FeedSort.trending => 'trending',
        _FeedSort.newest => 'new',
      };

  static _FeedSort fromQuery(String? value) => switch (value) {
        'new' || 'newest' => _FeedSort.newest,
        _ => _FeedSort.trending,
      };
}

class _NewPostsBanner extends StatelessWidget {
  const _NewPostsBanner({required this.count, required this.onTap});
  final int count;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        decoration: BoxDecoration(
          color: Theme.of(context).colorScheme.primary,
          borderRadius: BorderRadius.circular(20),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withValues(alpha: 0.2),
              blurRadius: 8,
              offset: const Offset(0, 4),
            ),
          ],
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.arrow_upward, color: Colors.white, size: 16),
            const SizedBox(width: 8),
            Text(
              '$count yeni gönderi',
              style: const TextStyle(
                color: Colors.white,
                fontWeight: FontWeight.bold,
                fontSize: 13,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
