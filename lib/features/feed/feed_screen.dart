import 'package:firebase_crashlytics/firebase_crashlytics.dart';
import 'package:flutter/foundation.dart' hide Category;
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../core/auth/auth_service.dart';
import '../../core/providers.dart';
import '../../core/layout/breakpoints.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/karar_chip.dart';
import '../../shared/widgets/karar_circle_button.dart';
import '../../shared/widgets/karar_pressable.dart';
import '../../shared/widgets/karar_sheet.dart';
import '../../shared/widgets/karar_text_tabs.dart';
import '../../shared/widgets/karma_badge.dart';
import '../../shared/widgets/login_nudge.dart';
import '../../core/history/history_provider.dart';
import '../../shared/widgets/karar_logo.dart';
import '../../core/network/connectivity_provider.dart';
import '../ads/banner_ad_widget.dart';
import 'categories_provider.dart';
import 'feed_provider.dart';
import 'post_card.dart';
import 'trend_topics_panel.dart';
import 'weekly_featured_card.dart';
import 'widgets/feed_skeletons.dart';

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
  var _scrolled = false;
  String? _lastRouteState;
  final _scrollController = ScrollController();
  ProviderSubscription<AsyncValue<ConnectivityStatus>>? _connectivitySub;
  final _itemKeys = <int, GlobalKey>{};
  int _scrollDepthMilestoneFired = 0;
  bool _firstVoteCoachmarkDone = true;

  static const _fabThreshold = 400.0;
  static const _scrollMilestones = [5, 10, 25, 50];
  static const _firstVoteCoachmarkKey = 'first_vote_coachmark_done';

  @override
  void initState() {
    super.initState();
    _scrollController.addListener(_onScroll);
    _loadFirstVoteCoachmarkState();

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
      const estimatedItemHeight = 440.0;
      const separatorHeight = KararSpace.feedGap;
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

    // Hairline under the top bar once the list has moved.
    final scrolled = _scrollController.position.pixels > 4;
    if (scrolled != _scrolled) setState(() => _scrolled = scrolled);

    // Top bar visibility (hide on scroll down, show on scroll up)
    if (_scrollController.position.userScrollDirection ==
        ScrollDirection.reverse) {
      if (_showTopBar) setState(() => _showTopBar = false);
    } else if (_scrollController.position.userScrollDirection ==
        ScrollDirection.forward) {
      if (!_showTopBar) setState(() => _showTopBar = true);
    }

    // Scroll depth milestones — fire once per milestone per session
    final postCount = ref.read(feedProvider).posts.length;
    if (postCount == 0) return;
    final maxExtent = _scrollController.position.maxScrollExtent;
    if (maxExtent <= 0) return;
    final ratio = _scrollController.position.pixels / maxExtent;
    final estimatedPosition = (ratio * postCount).round().clamp(0, postCount);
    for (final milestone in _scrollMilestones) {
      if (estimatedPosition >= milestone &&
          milestone > _scrollDepthMilestoneFired) {
        _scrollDepthMilestoneFired = milestone;
        ref.read(analyticsServiceProvider).logFeedScrollDepth(
              milestone: milestone,
              positionReached: estimatedPosition,
              sort: _sortMode.queryValue,
              categoryId: _selectedCategoryId == 0
                  ? null
                  : _selectedCategoryId.toString(),
            );
        break;
      }
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

  Future<void> _loadFirstVoteCoachmarkState() async {
    final prefs = await SharedPreferences.getInstance();
    if (!mounted) return;
    setState(() {
      _firstVoteCoachmarkDone = prefs.getBool(_firstVoteCoachmarkKey) ?? false;
    });
  }

  Future<void> _completeFirstVoteCoachmark() async {
    if (_firstVoteCoachmarkDone) return;
    setState(() => _firstVoteCoachmarkDone = true);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_firstVoteCoachmarkKey, true);
  }

  Future<void> _voteFromFeed(Post post, VoteType voteType) async {
    await _completeFirstVoteCoachmark();
    ref.read(feedProvider.notifier).vote(post.id, voteType.name);
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
    setState(() {
      _selectedCategoryId = id;
      _scrollDepthMilestoneFired = 0;
    });
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

    showKararSheet<void>(
      context,
      builder: (context) => SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            KararSheetHeader(
              title: category.name,
              onClose: () => Navigator.pop(context),
            ),
            ListTile(
              leading: Icon(
                isFollowed ? Icons.star_rounded : Icons.star_border_rounded,
              ),
              title: Text(isFollowed ? 'Takibi Bırak' : 'Takip Et'),
              onTap: () {
                Navigator.pop(context);
                _toggleFollow(category);
              },
            ),
            ListTile(
              leading: Icon(
                isMuted ? Icons.volume_up_rounded : Icons.volume_off_rounded,
              ),
              title: Text(isMuted ? 'Sessizden Çıkar' : 'Sessize Al'),
              subtitle: isMuted
                  ? null
                  : const Text(
                      'Bu kategorideki gönderileri akışında görmezsin.'),
              onTap: () {
                Navigator.pop(context);
                _toggleMute(category);
              },
            ),
            const SizedBox(height: 20),
          ],
        ),
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
            content: Text(
              next.actionError!,
              style: const TextStyle(color: Colors.white),
            ),
            backgroundColor: context.kc.haksizFill,
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
    final showFirstVoteCoachmark = !_firstVoteCoachmarkDone &&
        filtered.isNotEmpty &&
        !filtered.any((post) => post.myVote != null);

    final categoriesAsync = ref.watch(categoriesProvider);
    final followedIds = ref.watch(followedCategoriesProvider);
    final rawCategories = categoriesAsync.valueOrNull ?? [];

    final categories = rawCategories.isEmpty
        ? rawCategories
        : [
            ...rawCategories.where((c) => c.id == 0),
            ...rawCategories
                .where((c) => c.id != 0 && followedIds.contains(c.id)),
            ...rawCategories
                .where((c) => c.id != 0 && !followedIds.contains(c.id)),
          ];

    final connectivity = ref.watch(connectivityProvider);
    final isDisconnected =
        connectivity.value == ConnectivityStatus.isDisconnected;
    final isDesktop = context.isDesktop;
    final user = ref.watch(currentUserProvider);

    final feedArea = _buildFeedArea(
      feedState,
      filtered,
      focusedIndex,
      showFirstVoteCoachmark,
      horizontalPadding: isDesktop ? 8 : KararSpace.gutter,
    );

    return Scaffold(
      appBar: isDesktop ? null : _buildTopBar(user),
      floatingActionButton: Padding(
        // The shell's floating nav is part of the bottom padding; the FAB
        // must clear it.
        padding: EdgeInsets.only(bottom: context.navBottomInset),
        child: _BackToTopButton(visible: _showFab, onPressed: _scrollToTop),
      ),
      body: isDesktop
          ? _buildDesktopLayout(
              feedArea: feedArea,
              categories: categories,
              followedIds: followedIds,
              isDisconnected: isDisconnected,
            )
          : CenteredContent(
              maxWidth: 640,
              child: Column(
                children: [
                  if (isDisconnected) _buildDisconnectedBanner(),
                  const BannerAdWidget(),
                  AnimatedSize(
                    duration: KararMotion.d(context, KararMotion.nav),
                    curve: KararMotion.standard,
                    alignment: Alignment.topCenter,
                    child: _showTopBar
                        ? Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              const WeeklyFeaturedCard(),
                              _buildSortTabs(
                                padding:
                                    const EdgeInsets.fromLTRB(20, 12, 20, 0),
                              ),
                              _buildCategoryRow(
                                categories,
                                followedIds,
                                padding:
                                    const EdgeInsets.fromLTRB(16, 6, 16, 10),
                              ),
                            ],
                          )
                        : const SizedBox(width: double.infinity),
                  ),
                  Expanded(child: feedArea),
                ],
              ),
            ),
    );
  }

  PreferredSizeWidget _buildTopBar(AuthUser? user) {
    final kc = context.kc;
    return AppBar(
      automaticallyImplyLeading: false,
      centerTitle: false,
      titleSpacing: 16,
      backgroundColor: kc.canvas,
      shape: _scrolled || !_showTopBar
          ? Border(bottom: BorderSide(color: kc.outlineVariant))
          : null,
      title: Align(
        alignment: Alignment.centerLeft,
        child: KararPressable(
          onTap: () {
            if (GoRouterState.of(context).uri.toString() == '/') {
              _scrollToTop();
            } else {
              context.go('/');
            }
          },
          semanticLabel: 'karar',
          pressedScale: 0.97,
          focusRadius: 20,
          child: const KararLogo(size: LogoSize.medium),
        ),
      ),
      actions: [
        IconButton(
          tooltip: 'Ara',
          onPressed: () => context.push('/search'),
          icon: Icon(Icons.search, color: kc.text1),
        ),
        const SizedBox(width: 4),
        _IdentityPill(user: user),
      ],
    );
  }

  Widget _buildSortTabs({required EdgeInsets padding}) {
    return Align(
      alignment: Alignment.centerLeft,
      child: KararTextTabs(
        labels: const ['Trend', 'Yeni', 'Tartışmalı'],
        index: _FeedSort.values.indexOf(_sortMode),
        onChanged: (i) => _onSortChanged(_FeedSort.values[i]),
        padding: padding,
      ),
    );
  }

  Widget _buildCategoryRow(
    List<Category> categories,
    Set<int> followedIds, {
    required EdgeInsets padding,
  }) {
    if (categories.isEmpty) return const SizedBox.shrink();
    return SizedBox(
      height: 48 + padding.vertical,
      child: ListView.separated(
        padding: padding,
        scrollDirection: Axis.horizontal,
        itemCount: categories.length,
        separatorBuilder: (_, __) => const SizedBox(width: 8),
        itemBuilder: (context, index) {
          final category = categories[index];
          final isFollowed =
              category.id != 0 && followedIds.contains(category.id);
          return Center(
            child: KararChip(
              label: category.name,
              icon: CategoryStyle.of(category).icon,
              selected: _selectedCategoryId == category.id,
              trailing: isFollowed
                  ? const Icon(Icons.star_rounded, size: 12)
                  : null,
              onTap: () => _onCategoryChanged(category.id),
              onLongPress: category.id == 0
                  ? null
                  : () {
                      KararHaptics.longPress();
                      _showCategoryOptions(category);
                    },
            ),
          );
        },
      ),
    );
  }

  Widget _buildDesktopLayout({
    required Widget feedArea,
    required List<Category> categories,
    required Set<int> followedIds,
    required bool isDisconnected,
  }) {
    final kc = context.kc;
    final wide = context.isSideNavExpanded;
    final railWidth = wide ? 300.0 : 280.0;
    final gap = wide ? 32.0 : 24.0;

    return LayoutBuilder(
      builder: (context, constraints) {
        final showRail = constraints.maxWidth >= 560 + gap + railWidth;
        final feedWidth = showRail
            ? (constraints.maxWidth - railWidth - gap - 48).clamp(560.0, 640.0)
            : constraints.maxWidth.clamp(0.0, 640.0);

        final feedColumn = Column(
          children: [
            if (isDisconnected) _buildDisconnectedBanner(),
            const BannerAdWidget(),
            // Sticky header: tabs + category chips.
            AnimatedContainer(
              duration: KararMotion.d(context, KararMotion.fast),
              decoration: BoxDecoration(
                color: kc.canvas,
                border: Border(
                  bottom: BorderSide(
                    color: _scrolled
                        ? kc.outlineVariant
                        : kc.outlineVariant.withValues(alpha: 0),
                  ),
                ),
              ),
              padding: const EdgeInsets.only(top: 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _buildSortTabs(
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                  ),
                  _buildCategoryRow(
                    categories,
                    followedIds,
                    padding: const EdgeInsets.fromLTRB(8, 6, 8, 10),
                  ),
                ],
              ),
            ),
            Expanded(child: feedArea),
          ],
        );

        return Align(
          alignment: Alignment.topCenter,
          child: SizedBox(
            width: showRail ? feedWidth + gap + railWidth : feedWidth,
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SizedBox(width: feedWidth, child: feedColumn),
                if (showRail) ...[
                  SizedBox(width: gap),
                  SizedBox(width: railWidth, child: _buildRightPanel()),
                ],
              ],
            ),
          ),
        );
      },
    );
  }

  Widget _buildRightPanel() {
    return SingleChildScrollView(
      padding: EdgeInsets.fromLTRB(0, 20, 0, context.navBottomInset + 24),
      child: const Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          WeeklyFeaturedCard(margin: EdgeInsets.zero),
          SizedBox(height: 16),
          TrendTopicsPanel(margin: EdgeInsets.zero),
          SizedBox(height: 16),
          _FooterLinks(),
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
    final kc = context.kc;
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 4),
      child: Container(
        width: double.infinity,
        padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 14),
        decoration: BoxDecoration(
          color: kc.warnSoft,
          borderRadius: KararRadius.all(KararRadius.input),
        ),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            ExcludeSemantics(
              child: Icon(Icons.wifi_off_rounded, size: 16, color: kc.warnText),
            ),
            const SizedBox(width: 8),
            Flexible(
              child: Text(
                'İnternet bağlantısı yok',
                style: AppTypography.meta.copyWith(
                  fontWeight: FontWeight.w800,
                  letterSpacing: 0,
                  color: kc.warnText,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildOfflineFallback() {
    final kc = context.kc;
    return _FeedMessage(
      icon: Icons.wifi_off_rounded,
      discColor: kc.warnSoft,
      iconColor: kc.warnText,
      title: 'İnternet bağlantısı yok',
      message: 'Bağlantı gelince akış otomatik yenilenecek.',
      action: KararButton(
        label: 'Tekrar Dene',
        icon: Icons.refresh_rounded,
        variant: KararButtonVariant.outlined,
        expand: false,
        onPressed: _onRefresh,
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

  /// The list (or its loading/empty/error state) with the floating
  /// "N yeni gönderi" pill on top.
  Widget _buildFeedArea(
    FeedState feedState,
    List<Post> posts,
    int? focusedIndex,
    bool showFirstVoteCoachmark, {
    required double horizontalPadding,
  }) {
    return Stack(
      children: [
        Positioned.fill(
          child: _buildBody(
            feedState,
            posts,
            focusedIndex,
            showFirstVoteCoachmark,
            horizontalPadding: horizontalPadding,
          ),
        ),
        Positioned(
          top: 8,
          left: 0,
          right: 0,
          child: Center(
            child: AnimatedSwitcher(
              duration: KararMotion.d(context, KararMotion.base),
              switchInCurve: KararMotion.standard,
              switchOutCurve: KararMotion.exit,
              transitionBuilder: (child, animation) => SlideTransition(
                position: Tween<Offset>(
                  begin: const Offset(0, -0.8),
                  end: Offset.zero,
                ).animate(animation),
                child: FadeTransition(opacity: animation, child: child),
              ),
              child: feedState.newPostsCount > 0
                  ? _NewPostsBanner(
                      key: ValueKey(feedState.newPostsCount),
                      count: feedState.newPostsCount,
                      onTap: () async {
                        await _scrollToTop();
                        await ref.read(feedProvider.notifier).refresh();
                      },
                    )
                  : const SizedBox.shrink(),
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildBody(
    FeedState feedState,
    List<Post> posts,
    int? focusedIndex,
    bool showFirstVoteCoachmark, {
    required double horizontalPadding,
  }) {
    final isDesktop = context.isDesktop;
    final bottomPadding = context.navBottomInset + 24;

    if (feedState.isLoading && posts.isEmpty) {
      return ListView.separated(
        padding: EdgeInsets.fromLTRB(
          horizontalPadding,
          4,
          horizontalPadding,
          bottomPadding,
        ),
        physics: const NeverScrollableScrollPhysics(),
        itemCount: 3,
        separatorBuilder: (_, __) => const SizedBox(height: KararSpace.feedGap),
        itemBuilder: (_, __) => FeedCardSkeleton(
          height: isDesktop ? 400 : 440,
          compact: isDesktop,
        ),
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
      final kc = context.kc;
      return _FeedMessage(
        icon: Icons.balance_rounded,
        discColor: kc.primaryContainer,
        iconColor: kc.primary,
        title: 'Henüz paylaşım yok',
        message: 'Bir durum anlat, topluluk Haklı mı Haksız mı karar versin.',
        action: KararButton(
          label: 'Paylaşım Yap',
          icon: Icons.add_rounded,
          expand: false,
          onPressed: () => context.go('/create'),
        ),
      );
    }

    final reduceMotion = KararMotion.reduce(context);
    final seenPosts = ref.watch(historyProvider);

    final listView = RefreshIndicator(
      onRefresh: _onRefresh,
      child: BackdropGroup(
        child: ListView.separated(
          controller: _scrollController,
          padding: EdgeInsets.fromLTRB(0, 4, 0, bottomPadding),
          itemCount: posts.length + (feedState.isLoadingMore ? 1 : 0),
          separatorBuilder: (_, __) =>
              const SizedBox(height: KararSpace.feedGap),
          itemBuilder: (context, index) {
            if (index >= posts.length) {
              return Padding(
                padding: EdgeInsets.symmetric(horizontal: horizontalPadding),
                child: FeedCardSkeleton(
                  height: isDesktop ? 400 : 440,
                  compact: isDesktop,
                ),
              );
            }
            final post = posts[index];
            final itemKey = _itemKeys.putIfAbsent(index, () => GlobalKey());
            Widget card = PostCard(
              post: post,
              isSeen: seenPosts.contains(post.id),
              isFocused: focusedIndex == index,
              showFirstVoteCoachmark: showFirstVoteCoachmark && index == 0,
              onDismissFirstVoteCoachmark: _completeFirstVoteCoachmark,
              onVote: (voteType) => _voteFromFeed(post, voteType),
              onTap: () =>
                  context.push('/posts/${post.id}?source=feed', extra: post),
            );
            if (!reduceMotion) {
              card = card
                  .animate(
                    delay: Duration(milliseconds: (40 * index.clamp(0, 5))),
                  )
                  .fadeIn(
                    duration: KararMotion.base,
                    curve: KararMotion.standard,
                  )
                  .slideY(
                    begin: 0.04,
                    end: 0,
                    duration: KararMotion.base,
                    curve: KararMotion.standard,
                  );
            }
            return KeyedSubtree(
              key: itemKey,
              child: Padding(
                padding: EdgeInsets.symmetric(horizontal: horizontalPadding),
                child: card,
              ),
            );
          },
        ),
      ),
    );

    if (feedState.isFallback) {
      return Column(
        children: [
          Padding(
            padding: EdgeInsets.fromLTRB(
              horizontalPadding,
              4,
              horizontalPadding,
              12,
            ),
            child: const _FallbackBanner(),
          ),
          Expanded(child: listView),
        ],
      );
    }
    return listView;
  }
}

/// Logo-row identity pill (§5.3): avatar + username + karma badge → profile;
/// guest: person glyph + "Giriş Yap" → login.
class _IdentityPill extends StatelessWidget {
  const _IdentityPill({required this.user});

  final AuthUser? user;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final user = this.user;
    final textStyle = AppTypography.labelS.copyWith(
      fontWeight: FontWeight.w800,
      color: kc.text1,
    );

    final Widget content;
    final VoidCallback onTap;
    final String label;
    if (user == null) {
      label = 'Giriş Yap';
      onTap = () => context.push('/auth/login');
      content = Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 32,
            height: 32,
            decoration: BoxDecoration(color: kc.surface, shape: BoxShape.circle),
            alignment: Alignment.center,
            child: Icon(
              Icons.person_outline_rounded,
              size: 18,
              color: kc.text2,
            ),
          ),
          const SizedBox(width: 6),
          Text('Giriş Yap', style: textStyle, maxLines: 1),
        ],
      );
    } else {
      label = 'Profilim, ${user.username}';
      onTap = () => context.go('/profile');
      content = Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          KararAvatar(
            username: user.username,
            radius: 16,
            ring: KararAvatarRing.surface,
          ),
          const SizedBox(width: 6),
          ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 96),
            child: Text(
              user.username,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: textStyle,
            ),
          ),
          const SizedBox(width: 4),
          KarmaBadge(karma: user.karma, size: 14),
        ],
      );
    }

    return KararPressable(
      onTap: onTap,
      semanticLabel: label,
      pressedScale: 0.97,
      focusRadius: 20,
      child: Container(
        constraints: const BoxConstraints(minHeight: 40),
        padding: const EdgeInsets.fromLTRB(4, 2, 12, 2),
        decoration: BoxDecoration(
          color: kc.muted,
          borderRadius: KararRadius.all(20),
        ),
        child: content,
      ),
    );
  }
}

/// Back-to-top button: 48 px surface circle with the float shadow.
class _BackToTopButton extends StatelessWidget {
  const _BackToTopButton({required this.visible, required this.onPressed});

  final bool visible;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final brightness = Theme.of(context).brightness;
    final hairline = KararShadow.hairlineFor(KararShadowKind.float, brightness);
    return IgnorePointer(
      ignoring: !visible,
      child: ExcludeSemantics(
        excluding: !visible,
        child: AnimatedSlide(
          duration: KararMotion.d(context, KararMotion.base),
          curve: KararMotion.standard,
          offset: visible ? Offset.zero : const Offset(0, 0.6),
          child: AnimatedOpacity(
            duration: KararMotion.fade(context, KararMotion.base),
            opacity: visible ? 1 : 0,
            child: DecoratedBox(
              decoration: ShapeDecoration(
                shape: CircleBorder(
                  side: hairline == null
                      ? BorderSide.none
                      : BorderSide(color: hairline),
                ),
                shadows: KararShadow.float(brightness),
              ),
              child: KararCircleButton(
                icon: Icons.keyboard_arrow_up_rounded,
                tooltip: 'Başa dön',
                onPressed: onPressed,
                size: 48,
                iconSize: 26,
                style: KararCircleStyle.muted,
                backgroundColor: kc.isDark ? kc.elevated : kc.surface,
                iconColor: kc.text1,
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Centred disc + title + message + action (empty / offline states).
class _FeedMessage extends StatelessWidget {
  const _FeedMessage({
    required this.icon,
    required this.discColor,
    required this.iconColor,
    required this.title,
    required this.message,
    this.action,
  });

  final IconData icon;
  final Color discColor;
  final Color iconColor;
  final String title;
  final String message;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return LayoutBuilder(
      builder: (context, constraints) => SingleChildScrollView(
        padding: EdgeInsets.fromLTRB(32, 24, 32, context.navBottomInset + 24),
        child: ConstrainedBox(
          constraints: BoxConstraints(
            minHeight: (constraints.maxHeight - context.navBottomInset - 48)
                .clamp(0.0, double.infinity),
          ),
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 320),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  ExcludeSemantics(
                    child: Container(
                      width: 88,
                      height: 88,
                      decoration: BoxDecoration(
                        color: discColor,
                        shape: BoxShape.circle,
                      ),
                      alignment: Alignment.center,
                      child: Icon(icon, size: 36, color: iconColor),
                    ),
                  ),
                  const SizedBox(height: 16),
                  Text(
                    title,
                    textAlign: TextAlign.center,
                    style: AppTypography.headline.copyWith(color: kc.text1),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    message,
                    textAlign: TextAlign.center,
                    style: AppTypography.body.copyWith(color: kc.text2),
                  ),
                  if (action != null) ...[
                    const SizedBox(height: 20),
                    action!,
                  ],
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _FallbackBanner extends StatelessWidget {
  const _FallbackBanner();

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Container(
      width: double.infinity,
      decoration: BoxDecoration(
        color: kc.muted,
        borderRadius: KararRadius.all(KararRadius.input),
      ),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      child: Row(
        children: [
          ExcludeSemantics(
            child: Icon(Icons.explore_outlined, size: 16, color: kc.text2),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              'Bu akışta henüz paylaşım yok — popüler gönderiler',
              style: AppTypography.bodyS.copyWith(color: kc.text2),
            ),
          ),
        ],
      ),
    );
  }
}

/// Desktop right-rail footer: legal links separated by " · ".
class _FooterLinks extends StatelessWidget {
  const _FooterLinks();

  static const _links = [
    ('Kullanım Koşulları', '/legal/terms'),
    ('Gizlilik Politikası', '/legal/privacy'),
    ('Topluluk Kuralları', '/legal/community'),
    ('İçerik Politikası', '/legal/content-policy'),
    ('Sık Sorulan Sorular', '/legal/faq'),
  ];

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final style = AppTypography.meta.copyWith(color: kc.text3);
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 4),
      child: Wrap(
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          for (var i = 0; i < _links.length; i++) ...[
            if (i > 0)
              ExcludeSemantics(child: Text(' · ', style: style)),
            TextButton(
              onPressed: () => context.push(_links[i].$2),
              style: TextButton.styleFrom(
                foregroundColor: kc.text3,
                minimumSize: const Size(0, 44),
                padding: const EdgeInsets.symmetric(horizontal: 4),
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                textStyle: style,
              ),
              child: Text(_links[i].$1),
            ),
          ],
        ],
      ),
    );
  }
}

enum _FeedSort {
  trending,
  newest,
  controversial;

  String get queryValue => switch (this) {
        _FeedSort.trending => 'trending',
        _FeedSort.newest => 'new',
        _FeedSort.controversial => 'controversial',
      };

  static _FeedSort fromQuery(String? value) => switch (value) {
        'new' || 'newest' => _FeedSort.newest,
        'controversial' => _FeedSort.controversial,
        _ => _FeedSort.trending,
      };
}

/// Floating "N yeni gönderi" pill (primaryFill, white, float shadow).
class _NewPostsBanner extends StatelessWidget {
  const _NewPostsBanner({super.key, required this.count, required this.onTap});
  final int count;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return KararPressable(
      onTap: onTap,
      semanticLabel: '$count yeni gönderi',
      pressedScale: 0.96,
      focusRadius: 20,
      child: Container(
        constraints: const BoxConstraints(minHeight: 40),
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        decoration: BoxDecoration(
          color: kc.primaryFill,
          borderRadius: KararRadius.all(20),
          boxShadow: KararShadow.float(Theme.of(context).brightness),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.arrow_upward_rounded, color: Colors.white, size: 16),
            const SizedBox(width: 6),
            Text(
              '$count yeni gönderi',
              style: AppTypography.labelS.copyWith(
                color: Colors.white,
                fontWeight: FontWeight.w800,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
