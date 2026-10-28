import 'dart:math' as math;

import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:shimmer/shimmer.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karar_circle_button.dart';
import '../../shared/widgets/karar_pressable.dart';
import '../../shared/widgets/karar_sheet.dart';
import '../../shared/widgets/karar_top_bar.dart';
import '../../shared/widgets/on_color_chip.dart';
import '../../shared/widgets/post_surface.dart';
import '../../shared/widgets/verdict_chip.dart';
import '../../shared/widgets/vote_rail.dart';
import '../post_detail/comment_list.dart';
import '../post_detail/post_detail_provider.dart';
import '../post_detail/share_picker_sheet.dart';
import 'discover_feed_provider.dart';
import 'discover_provider.dart';
import 'post_card.dart';

/// Keşfet (spec §6.3): an immersive, full-bleed vertical pager on
/// mobile/tablet and a centred phone frame with arrow buttons on desktop.
/// The "Bölümler" toggle switches to the curated sections list.
class DiscoverScreen extends ConsumerStatefulWidget {
  const DiscoverScreen({super.key});

  @override
  ConsumerState<DiscoverScreen> createState() => _DiscoverScreenState();
}

class _DiscoverScreenState extends ConsumerState<DiscoverScreen>
    with WidgetsBindingObserver {
  late final PageController _ctrl;
  int _currentIndex = 0;
  DateTime _pageEnteredAt = DateTime.now();
  bool _firstImpressionSent = false;
  String? _activePostId;
  int _snapDepthMilestoneFired = 0;
  bool _showSections = false;

  /// One backdrop group for every live blur on the pages (glass rail,
  /// chips) — kept stable so the group is not recreated on rebuilds.
  final _backdropKey = BackdropKey();

  static const _snapMilestones = [3, 7, 15, 30];

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _ctrl = PageController();
    _ctrl.addListener(_onScroll);
  }

  @override
  void dispose() {
    final items = ref.read(discoverFeedProvider).valueOrNull?.items ?? [];
    _handlePageLeave(_currentIndex, items);
    _ctrl.removeListener(_onScroll);
    _ctrl.dispose();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused) {
      final items = ref.read(discoverFeedProvider).valueOrNull?.items ?? [];
      _handlePageLeave(_currentIndex, items);
    } else if (state == AppLifecycleState.resumed) {
      final items = ref.read(discoverFeedProvider).valueOrNull?.items ?? [];
      _handlePageEnter(_currentIndex, items);
    }
  }

  void _onScroll() {
    if (!_ctrl.hasClients) return;
    final page = _ctrl.page?.round() ?? 0;
    if (page == _currentIndex) return;
    final items = ref.read(discoverFeedProvider).valueOrNull?.items ?? [];
    _handlePageLeave(_currentIndex, items);
    _currentIndex = page;
    _handlePageEnter(page, items);
    if (page >= items.length - 3) {
      ref.read(discoverFeedProvider.notifier).loadMore();
    }

    // Snap depth milestones (counted by cards seen, not position in list)
    for (final milestone in _snapMilestones) {
      if (page + 1 >= milestone && milestone > _snapDepthMilestoneFired) {
        _snapDepthMilestoneFired = milestone;
        ref.read(analyticsServiceProvider).logDiscoverSnapDepth(
              milestone: milestone,
              positionReached: page + 1,
            );
        break;
      }
    }
  }

  void _toggleSections() {
    if (!_showSections) {
      final items = ref.read(discoverFeedProvider).valueOrNull?.items ?? [];
      _handlePageLeave(_currentIndex, items);
    }
    setState(() => _showSections = !_showSections);
    if (!_showSections) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        final items = ref.read(discoverFeedProvider).valueOrNull?.items ?? [];
        _handlePageEnter(_currentIndex, items);
      });
    }
  }

  void _handlePageLeave(int index, List<DiscoverFeedItem> items) {
    if (index >= items.length) return;
    final item = items[index];
    if (_activePostId != item.post.id) return;

    final dwell = DateTime.now().difference(_pageEnteredAt).inSeconds;
    final repo = ref.read(postRepositoryProvider);
    final analytics = ref.read(analyticsServiceProvider);
    _activePostId = null;

    if (dwell >= 3) {
      repo.sendDiscoverEvent(
        postId: item.post.id,
        eventType: 'dwell',
        dwellSeconds: dwell,
        impressionToken: item.impressionToken,
        rankingReason: item.rankingReason,
      );
      analytics.logDiscoverDwell(
        postId: item.post.id,
        durationSeconds: dwell,
        position: index,
        rankingReason: item.rankingReason,
      );
    } else {
      repo.sendDiscoverEvent(
        postId: item.post.id,
        eventType: 'skip',
        impressionToken: item.impressionToken,
        rankingReason: item.rankingReason,
      );
      analytics.logDiscoverSkip(
        postId: item.post.id,
        durationSeconds: dwell,
        position: index,
        rankingReason: item.rankingReason,
      );
    }
  }

  void _handlePageEnter(int index, List<DiscoverFeedItem> items) {
    if (index >= items.length) return;
    final item = items[index];
    if (_activePostId == item.post.id) return;

    _activePostId = item.post.id;
    _pageEnteredAt = DateTime.now();
    ref.read(postRepositoryProvider).sendDiscoverEvent(
          postId: item.post.id,
          eventType: 'impression',
          impressionToken: item.impressionToken,
          rankingReason: item.rankingReason,
        );
    ref.read(analyticsServiceProvider).logDiscoverImpression(
          postId: item.post.id,
          position: index,
          rankingReason: item.rankingReason,
        );
  }

  @override
  Widget build(BuildContext context) {
    final feedAsync = ref.watch(discoverFeedProvider);

    ref.listen(discoverFeedProvider, (prev, next) {
      if (next is AsyncData<DiscoverFeedState> && !_firstImpressionSent) {
        final items = next.value.items;
        if (items.isNotEmpty) {
          _firstImpressionSent = true;
          _handlePageEnter(0, items);
        }
      }
    });

    if (_showSections) {
      return _buildCanvas(
        context,
        toggleIcon: Icons.explore_rounded,
        toggleTooltip: 'Akış',
        body: const _DiscoverSectionsView(),
      );
    }

    return feedAsync.when(
      data: (state) {
        if (state.items.isEmpty) {
          return _buildCanvas(
            context,
            body: Center(
              child: EmptyState(
                message:
                    'Şu an keşfedilecek içerik yok.\nBiraz sonra tekrar dene.',
                icon: Icons.explore_outlined,
                action: () => ref.invalidate(discoverFeedProvider),
                actionLabel: 'Yenile',
              ),
            ),
          );
        }
        return _buildImmersive(
          context,
          itemCount: state.items.length,
          child: _buildPager(state),
        );
      },
      loading: () => _buildImmersive(
        context,
        itemCount: 0,
        child: const _DiscoverLoadingSkeleton(),
      ),
      error: (error, _) => _buildCanvas(
        context,
        body: Center(
          child: ErrorView(
            message: 'Keşfet yüklenemedi',
            onRetry: () => ref.invalidate(discoverFeedProvider),
          ),
        ),
      ),
    );
  }

  /// Normal canvas page with a centred "Keşfet" app bar (sections view,
  /// empty and error states).
  Widget _buildCanvas(
    BuildContext context, {
    required Widget body,
    IconData toggleIcon = Icons.dashboard_rounded,
    String toggleTooltip = 'Bölümler',
  }) {
    return Scaffold(
      backgroundColor: context.kc.canvas,
      appBar: AppBar(
        // Canvas bar that gains the frosted fill + hairline (spec §2.8)
        // once the sections list scrolls under it, like the other tabs.
        backgroundColor: Colors.transparent,
        scrolledUnderElevation: 0,
        flexibleSpace: const KararTopBarBackground(),
        title: const Text('Keşfet'),
        centerTitle: true,
        actions: [
          IconButton(
            icon: Icon(toggleIcon),
            tooltip: toggleTooltip,
            onPressed: _toggleSections,
          ),
        ],
      ),
      body: body,
    );
  }

  /// Full-bleed dark pages (mobile/tablet) or the phone frame (desktop).
  Widget _buildImmersive(
    BuildContext context, {
    required int itemCount,
    required Widget child,
  }) {
    if (context.isDesktop) {
      return Scaffold(
        backgroundColor: context.kc.canvas,
        body: _DesktopFrame(
          controller: itemCount > 0 ? _ctrl : null,
          itemCount: itemCount,
          child: child,
        ),
      );
    }
    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.light,
      child: Scaffold(
        backgroundColor: KararColors.dark.canvas,
        body: child,
      ),
    );
  }

  Widget _buildPager(DiscoverFeedState state) {
    return BackdropGroup(
      backdropKey: _backdropKey,
      child: PageView.builder(
        controller: _ctrl,
        scrollDirection: Axis.vertical,
        itemCount: state.items.length + (state.isLoadingMore ? 1 : 0),
        itemBuilder: (context, index) {
          if (index >= state.items.length) {
            return const _DiscoverLoadingSkeleton();
          }
          final item = state.items[index];
          return _DiscoverCard(
            key: ValueKey(item.post.id),
            item: item,
            index: index,
            total: state.items.length,
            onOpenSections: _toggleSections,
            onVote: (voteType) => ref
                .read(discoverFeedProvider.notifier)
                .vote(item.post.id, voteType,
                    impressionToken: item.impressionToken),
            onNotInterested: () {
              ref.read(discoverFeedProvider.notifier).removeItem(item.post.id);
              if (_activePostId == item.post.id) {
                _activePostId = null;
              }
              WidgetsBinding.instance.addPostFrameCallback((_) {
                if (!mounted) return;
                final nextItems =
                    ref.read(discoverFeedProvider).valueOrNull?.items ?? [];
                if (nextItems.isEmpty) return;
                if (_currentIndex >= nextItems.length) {
                  _currentIndex = nextItems.length - 1;
                }
                _handlePageEnter(_currentIndex, nextItems);
              });
              ref.read(postRepositoryProvider).sendDiscoverEvent(
                    postId: item.post.id,
                    eventType: 'not_interested',
                    impressionToken: item.impressionToken,
                    rankingReason: item.rankingReason,
                  );
              ref.read(analyticsServiceProvider).logPostNotInterested(
                    postId: item.post.id,
                    rankingReason: item.rankingReason,
                  );
              ref.read(postRepositoryProvider).markNotInterested(item.post.id);
              if (_currentIndex >= state.items.length - 1) {
                _ctrl.previousPage(
                  duration: const Duration(milliseconds: 300),
                  curve: Curves.easeOut,
                );
              }
            },
            onCommentOpen: () {
              ref.read(postRepositoryProvider).sendDiscoverEvent(
                    postId: item.post.id,
                    eventType: 'comment_open',
                    impressionToken: item.impressionToken,
                    rankingReason: item.rankingReason,
                  );
              ref.read(analyticsServiceProvider).logDiscoverCommentOpen(
                    postId: item.post.id,
                    position: index,
                    rankingReason: item.rankingReason,
                  );
              _showCommentsSheet(
                context,
                item.post,
                impressionToken: item.impressionToken,
                rankingReason: item.rankingReason,
              );
            },
            onShare: () {
              ref.read(postRepositoryProvider).sendDiscoverEvent(
                    postId: item.post.id,
                    eventType: 'share',
                    impressionToken: item.impressionToken,
                    rankingReason: item.rankingReason,
                  );
              ref.read(analyticsServiceProvider).logPostShared(
                    postId: item.post.id,
                    category: item.post.category.name,
                  );
              SharePickerSheet.show(context, item.post);
            },
            onSave: (isSaved) async {
              final repo = ref.read(postRepositoryProvider);
              if (isSaved) {
                await repo.unsavePost(item.post.id);
              } else {
                await repo.savePost(item.post.id);
                ref.read(postRepositoryProvider).sendDiscoverEvent(
                      postId: item.post.id,
                      eventType: 'save',
                      impressionToken: item.impressionToken,
                      rankingReason: item.rankingReason,
                    );
              }
            },
          );
        },
      ),
    );
  }

  void _showCommentsSheet(
    BuildContext context,
    Post post, {
    required String impressionToken,
    required String rankingReason,
  }) {
    showKararSheet<void>(
      context,
      scrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => _DiscoverCommentsSheet(
        post: post,
        impressionToken: impressionToken,
        rankingReason: rankingReason,
      ),
    );
  }
}

// ─── Desktop phone frame ─────────────────────────────────────────────────────

/// Desktop (≥ 1024): the pager sits in a centred phone-sized frame (radius
/// 30, float shadow) with "Önceki" / "Sonraki" buttons on its right;
/// ArrowUp / ArrowDown do the same.
class _DesktopFrame extends StatelessWidget {
  const _DesktopFrame({
    required this.child,
    required this.controller,
    required this.itemCount,
  });

  final Widget child;
  final PageController? controller;
  final int itemCount;

  static const _arrowSize = 48.0;
  static const _arrowGap = 16.0;

  int _page() {
    final ctrl = controller;
    if (ctrl == null || !ctrl.hasClients) return 0;
    return (ctrl.page ?? ctrl.initialPage.toDouble()).round();
  }

  void _go(BuildContext context, int delta) {
    final ctrl = controller;
    if (ctrl == null || !ctrl.hasClients) return;
    final target = _page() + delta;
    if (target < 0 || target >= itemCount) return;
    if (KararMotion.reduce(context)) {
      ctrl.jumpToPage(target);
    } else if (delta < 0) {
      ctrl.previousPage(duration: KararMotion.nav, curve: KararMotion.standard);
    } else {
      ctrl.nextPage(duration: KararMotion.nav, curve: KararMotion.standard);
    }
  }

  @override
  Widget build(BuildContext context) {
    final brightness = Theme.of(context).brightness;
    final hairline = KararShadow.hairlineFor(KararShadowKind.float, brightness);
    final radius = KararRadius.all(KararRadius.card);

    Widget arrows(int page) => Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            KararCircleButton(
              icon: Icons.keyboard_arrow_up_rounded,
              tooltip: 'Önceki',
              size: _arrowSize,
              iconSize: 26,
              onPressed: controller != null && page > 0
                  ? () => _go(context, -1)
                  : null,
            ),
            const SizedBox(height: KararSpace.s12),
            KararCircleButton(
              icon: Icons.keyboard_arrow_down_rounded,
              tooltip: 'Sonraki',
              size: _arrowSize,
              iconSize: 26,
              onPressed: controller != null && page < itemCount - 1
                  ? () => _go(context, 1)
                  : null,
            ),
          ],
        );

    return LayoutBuilder(
      builder: (context, constraints) {
        final width = math.max(
          0.0,
          math.min(
            480.0,
            constraints.maxWidth - 48 - 2 * (_arrowSize + _arrowGap),
          ),
        );
        final height = math.max(0.0, constraints.maxHeight - 48);

        final frame = Container(
          width: width,
          height: height,
          clipBehavior: Clip.antiAlias,
          decoration: BoxDecoration(
            color: KararColors.dark.canvas,
            borderRadius: radius,
            boxShadow: KararShadow.float(brightness),
          ),
          foregroundDecoration: hairline == null
              ? null
              : BoxDecoration(
                  borderRadius: radius,
                  border: Border.all(color: hairline),
                ),
          child: MediaQuery.removePadding(
            context: context,
            removeTop: true,
            removeBottom: true,
            removeLeft: true,
            removeRight: true,
            child: child,
          ),
        );

        return CallbackShortcuts(
          bindings: {
            const SingleActivator(LogicalKeyboardKey.arrowUp): () =>
                _go(context, -1),
            const SingleActivator(LogicalKeyboardKey.arrowDown): () =>
                _go(context, 1),
          },
          child: Focus(
            autofocus: true,
            child: Center(
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  // Balances the arrow column so the frame stays centred.
                  const SizedBox(width: _arrowSize + _arrowGap),
                  frame,
                  const SizedBox(width: _arrowGap),
                  SizedBox(
                    width: _arrowSize,
                    child: controller == null
                        ? arrows(0)
                        : ListenableBuilder(
                            listenable: controller!,
                            builder: (context, _) => arrows(_page()),
                          ),
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }
}

// ─── One immersive page ──────────────────────────────────────────────────────

class _DiscoverCard extends ConsumerStatefulWidget {
  const _DiscoverCard({
    super.key,
    required this.item,
    required this.index,
    required this.total,
    required this.onVote,
    required this.onNotInterested,
    required this.onCommentOpen,
    required this.onShare,
    required this.onSave,
    required this.onOpenSections,
  });

  final DiscoverFeedItem item;
  final int index;
  final int total;
  final Future<void> Function(VoteType voteType) onVote;
  final VoidCallback onNotInterested;
  final VoidCallback onCommentOpen;
  final VoidCallback onShare;
  final Future<void> Function(bool isSaved) onSave;
  final VoidCallback onOpenSections;

  @override
  ConsumerState<_DiscoverCard> createState() => _DiscoverCardState();
}

class _DiscoverCardState extends ConsumerState<_DiscoverCard> {
  bool _isSaved = false;
  bool _isVoting = false;
  bool _expanded = false;

  /// The side that was tapped while the server confirms the vote (Keşfet is
  /// not optimistic): the rail shows a progress ring on it.
  VoteType? _pendingVote;

  /// Height of the top overlay row (chips + sections button).
  static const _topBarHeight = 64.0;

  /// Room for the 3 px focus ring + 3 px gap inside the scrolling block.
  static const _ringRoom = 6.0;

  /// Photo pages — top scrim: black 45 % under the status bar, eased to 0
  /// over the chip row + [_topScrimTail] px. On a white sky the chips
  /// (28 % glass + this scrim) keep their white labels ≥ 4.5:1.
  static const _topScrimAlpha = 0.45;
  static const _topScrimTail = 56.0;

  /// Photo pages — bottom scrim behind the text block: eased in over
  /// [_blockScrimLead] px above the block to 55 % black at the block's top
  /// edge (white 14 px meta 4.75:1 over pure white), then 72 % at the
  /// bottom. The photo above the block stays untouched.
  static const _blockScrimLead = 88.0;
  static const _blockScrimTopAlpha = 0.55;
  static const _blockScrimBottomAlpha = 0.72;

  @override
  void initState() {
    super.initState();
    _isSaved = widget.item.post.isSaved;
  }

  @override
  void didUpdateWidget(_DiscoverCard old) {
    super.didUpdateWidget(old);
    if (old.item.post.id != widget.item.post.id) {
      _isSaved = widget.item.post.isSaved;
      _expanded = false;
    }
  }

  Future<void> _handleVote(VoteType type) async {
    if (_isVoting) return;
    setState(() {
      _isVoting = true;
      _pendingVote = type;
    });
    await widget.onVote(type);
    if (mounted) {
      setState(() {
        _isVoting = false;
        _pendingVote = null;
      });
    }
  }

  Future<void> _toggleSave() async {
    final prev = _isSaved;
    setState(() => _isSaved = !_isSaved);
    try {
      await widget.onSave(prev);
    } catch (_) {
      if (mounted) setState(() => _isSaved = prev);
    }
  }

  void _openDetail() {
    final post = widget.item.post;
    context.push('/posts/${post.id}', extra: post);
  }

  @override
  Widget build(BuildContext context) {
    final post = widget.item.post;
    final isPhoto = PostSurface.isPhotoPost(post);
    final padding = MediaQuery.paddingOf(context);
    final bottomInset = context.navBottomInset;

    // Photo pages draw their own photo and scrims (below): the card scrim
    // of `PostSurface` is tuned for ~500 px cards and, stretched over a
    // full-height page, darkens everything below the upper third. Here the
    // photo stays clean; a short top scrim covers the status bar / chips
    // and a bottom scrim follows the text block's real height.
    final photoUrl = isPhoto ? PostSurface.imageUrlOf(post) : null;

    return ClipRect(
      child: PostSurface(
        post: post,
        radius: 0,
        liveBlurHost: isPhoto,
        // The gradient shows while the photo loads (and if it fails).
        showPhoto: false,
        showQuote: !isPhoto,
        child: LayoutBuilder(
          builder: (context, constraints) {
            final pageHeight = constraints.maxHeight;
            final topReserved = padding.top + _topBarHeight;

            // Pick the largest rail that fits between the top bar and the
            // bottom inset (small/landscape viewports get the smaller rail).
            final railRoom = pageHeight - topReserved - bottomInset - 24 - 8;
            final railSize = railRoom >= VoteRailMetrics.fullscreen.height
                ? VoteRailSize.fullscreen
                : railRoom >= VoteRailMetrics.card.height
                    ? VoteRailSize.card
                    : VoteRailSize.compact;
            final rail = VoteRailMetrics.of(railSize);

            final blockRoom = math.max(
              0.0,
              pageHeight - topReserved - KararSpace.s8 - (bottomInset + 20),
            );
            final blockMaxHeight =
                _expanded ? math.min(blockRoom, pageHeight * 0.55) : blockRoom;

            return SizedBox(
              width: constraints.maxWidth,
              height: pageHeight,
              child: Stack(
                children: [
                  if (photoUrl != null) ...[
                    Positioned.fill(
                      child: RepaintBoundary(
                        child: _DiscoverPhoto(url: photoUrl),
                      ),
                    ),
                    // Top scrim: solid under the status bar, then eased
                    // out below the chip row (chips stay ≥ 4.5:1 together
                    // with their own 28 % glass fill).
                    Positioned(
                      top: 0,
                      left: 0,
                      right: 0,
                      height: topReserved + _topScrimTail,
                      child: _PhotoScrim(
                        fromBottom: false,
                        denseAlpha: _topScrimAlpha,
                        hold: padding.top,
                        fade: _topBarHeight + _topScrimTail,
                      ),
                    ),
                  ],
                  Positioned(
                    top: 0,
                    left: 0,
                    right: 0,
                    child: SafeArea(
                      bottom: false,
                      child: SizedBox(
                        height: _topBarHeight,
                        child: Padding(
                          // Right inset 16 minus the 7 px hit slack around
                          // the 30 px sections button: both edges read 16.
                          padding: const EdgeInsets.fromLTRB(16, 0, 9, 0),
                          child: _TopOverlay(
                            rankingReason: widget.item.rankingReason,
                            index: widget.index,
                            total: widget.total,
                            onPhoto: isPhoto,
                            onNotInterested: widget.onNotInterested,
                            onOpenSections: widget.onOpenSections,
                          ),
                        ),
                      ),
                    ),
                  ),
                  Positioned(
                    left: 0,
                    right: 0,
                    bottom: 0,
                    child: Stack(
                      clipBehavior: Clip.none,
                      children: [
                        // Bottom scrim sized to the text block: it fades in
                        // over `_blockScrimLead` px above the block and
                        // reaches ≥ 55 % black at the author row, so white
                        // meta text stays ≥ 4.5:1 even over a white photo.
                        if (photoUrl != null)
                          const Positioned(
                            left: 0,
                            right: 0,
                            top: -_blockScrimLead,
                            bottom: 0,
                            child: _PhotoScrim(
                              fromBottom: true,
                              denseAlpha: _blockScrimBottomAlpha,
                              fade: _blockScrimLead,
                              fadeAlpha: _blockScrimTopAlpha,
                            ),
                          ),
                        Padding(
                          // The scrollable block adds _ringRoom on each side
                          // so focus rings are not clipped; text still
                          // starts 20 px from the edge and ends 24 px before
                          // the rail.
                          padding: EdgeInsets.fromLTRB(
                            KararSpace.s20 - _ringRoom,
                            0,
                            rail.width + 24 - _ringRoom,
                            bottomInset + 20,
                          ),
                          child: Align(
                            alignment: Alignment.bottomLeft,
                            child: ConstrainedBox(
                              constraints: BoxConstraints(
                                maxWidth: 560 + 2 * _ringRoom,
                                maxHeight: blockMaxHeight,
                              ),
                              child: _buildTextBlock(context, post),
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                  Positioned(
                    right: 0,
                    bottom: bottomInset + 24,
                    child: VoteRail(
                      size: railSize,
                      hakliCount: post.voteCountHakli,
                      haksizCount: post.voteCountHaksiz,
                      commentCount: post.commentCount,
                      myVote: post.myVote,
                      onVote: (type) => _handleVote(type),
                      pendingVote: _isVoting ? _pendingVote : null,
                      onComment: widget.onCommentOpen,
                      onSave: _toggleSave,
                      isSaved: _isSaved,
                      onShare: widget.onShare,
                      liveBlur: isPhoto,
                      unvoteLabels: true,
                      hakliKey:
                          ValueKey('discover_vote_hakli_button_${post.id}'),
                      haksizKey:
                          ValueKey('discover_vote_haksiz_button_${post.id}'),
                    ),
                  ),
                ],
              ),
            );
          },
        ),
      ),
    );
  }

  Widget _buildTextBlock(BuildContext context, Post post) {
    final onColor = context.kc.onColor;
    final upper = Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _AuthorRow(post: post),
        const SizedBox(height: KararSpace.s10),
        KararPressable(
          onTap: _openDetail,
          semanticLabel: post.title,
          semanticHint: 'Detayları gör',
          pressedScale: 0.98,
          focusRadius: KararRadius.xs,
          child: Text(
            post.title,
            maxLines: 4,
            overflow: TextOverflow.ellipsis,
            style: AppTypography.titleXL.copyWith(color: onColor),
          ),
        ),
        if (post.content.isNotEmpty) ...[
          const SizedBox(height: KararSpace.s8),
          _ExpandableContent(
            text: post.content,
            expanded: _expanded,
            onToggle: () => setState(() => _expanded = !_expanded),
          ),
        ],
      ],
    );

    final Widget column;
    if (post.tags.isEmpty) {
      column = Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          upper,
          const SizedBox(height: KararSpace.s12),
          VerdictChip(post: post),
        ],
      );
    } else {
      // Tags keep a full 44 px hit height without 44 px of visual slack:
      // the tag row's hit box (_TagRow.boxHeight, text centred) slides
      // under the text above by _TagRow.overlapAbove (the text above keeps
      // that strip — it is hit-tested first) and over the non-interactive
      // VerdictChip below by _TagRow.overlapBelow. The column is laid out
      // bottom-up so the hit-test order is text → tags → verdict, and the
      // overlap shows up as empty space above the author row (invisible:
      // the block is anchored to the bottom).
      // Visual gaps: content → tags ≈ 8, tags → verdict 12.
      column = Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        verticalDirection: VerticalDirection.up,
        children: [
          VerdictChip(post: post),
          Transform.translate(
            offset: const Offset(0, _TagRow.overlapBelow),
            child: _TagRow(tags: post.tags),
          ),
          Transform.translate(
            offset: const Offset(
              0,
              _TagRow.overlapBelow + _TagRow.overlapAbove,
            ),
            child: upper,
          ),
        ],
      );
    }

    Widget block = SingleChildScrollView(
      physics: const ClampingScrollPhysics(),
      padding: EdgeInsets.fromLTRB(
        _ringRoom,
        _expanded ? KararSpace.s24 : 0,
        _ringRoom,
        0,
      ),
      child: column,
    );
    if (_expanded) {
      // Top fade while the expanded block scrolls.
      block = ShaderMask(
        blendMode: BlendMode.dstIn,
        shaderCallback: (rect) => LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          // Alpha mask only (dstIn): transparent → opaque.
          colors: [onColor.withValues(alpha: 0), onColor],
          stops: [0, rect.height <= 0 ? 0 : math.min(1, 24 / rect.height)],
        ).createShader(rect),
        child: block,
      );
    }
    return block;
  }
}

/// Full-bleed Keşfet photo (`BoxFit.cover`). Decoded at the page height so
/// tall pages are not upscaled from a width-sized decode. While loading or
/// on error nothing is drawn, so the category gradient underneath shows.
class _DiscoverPhoto extends StatelessWidget {
  const _DiscoverPhoto({required this.url});

  final String url;

  @override
  Widget build(BuildContext context) {
    return ExcludeSemantics(
      child: LayoutBuilder(
        builder: (context, constraints) {
          final dpr = MediaQuery.devicePixelRatioOf(context);
          final height = constraints.maxHeight.isFinite
              ? constraints.maxHeight
              : MediaQuery.sizeOf(context).height;
          final cacheHeight = math.min(2048, math.max(1, (height * dpr).round()));
          return CachedNetworkImage(
            imageUrl: url,
            fit: BoxFit.cover,
            width: double.infinity,
            height: double.infinity,
            memCacheHeight: cacheHeight,
            fadeInDuration: KararMotion.d(context, KararMotion.base),
            placeholder: (context, url) => const SizedBox.shrink(),
            errorWidget: (context, url, error) => const SizedBox.shrink(),
          );
        },
      ),
    );
  }
}

/// Eased black scrim over a photo (decorative, ignores pointers).
///
/// The dense end ([fromBottom] = bottom edge, else top edge) holds
/// [denseAlpha] for [hold] px, then the alpha moves linearly to [fadeAlpha]
/// where the last [fade] px begin, and eases out to 0 at the light end.
class _PhotoScrim extends StatelessWidget {
  const _PhotoScrim({
    required this.fromBottom,
    required this.denseAlpha,
    required this.fade,
    this.fadeAlpha,
    this.hold = 0,
  });

  final bool fromBottom;
  final double denseAlpha;
  final double fade;
  final double? fadeAlpha;
  final double hold;

  static const _easeSteps = 6;

  @override
  Widget build(BuildContext context) {
    final veil = context.kc.photoVeil;
    return IgnorePointer(
      child: ExcludeSemantics(
        child: LayoutBuilder(
          builder: (context, constraints) {
            final h = constraints.maxHeight;
            if (!h.isFinite || h <= 0) return const SizedBox.shrink();
            final fadeTo = fadeAlpha ?? denseAlpha;
            final fadeStart = ((h - fade) / h).clamp(0.0, 1.0);
            final holdEnd = (hold / h).clamp(0.0, fadeStart);

            final stops = <double>[0];
            final alphas = <double>[denseAlpha];
            if (holdEnd > 0) {
              stops.add(holdEnd);
              alphas.add(denseAlpha);
            }
            if (fadeStart > holdEnd) {
              stops.add(fadeStart);
              alphas.add(fadeTo);
            }
            // Ease out over the fade (no hard band where it ends).
            for (var i = 1; i <= _easeSteps; i++) {
              final t = i / _easeSteps;
              stops.add(fadeStart + (1 - fadeStart) * t);
              alphas.add(fadeTo * (1 - Curves.easeInOut.transform(t)));
            }

            return DecoratedBox(
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  begin: fromBottom
                      ? Alignment.bottomCenter
                      : Alignment.topCenter,
                  end: fromBottom ? Alignment.topCenter : Alignment.bottomCenter,
                  colors: [
                    for (final a in alphas) veil.withValues(alpha: a),
                  ],
                  stops: stops,
                ),
              ),
            );
          },
        ),
      ),
    );
  }
}

/// Glass chips over the page: ranking badge, page counter, "İlgilenmiyorum"
/// and the sections toggle.
class _TopOverlay extends StatelessWidget {
  const _TopOverlay({
    required this.rankingReason,
    required this.index,
    required this.total,
    required this.onPhoto,
    required this.onNotInterested,
    required this.onOpenSections,
  });

  final String rankingReason;
  final int index;
  final int total;
  final bool onPhoto;
  final VoidCallback onNotInterested;
  final VoidCallback onOpenSections;

  /// Visual height of every control in the row ([OnColorChip] is 30).
  static const _controlHeight = 30.0;

  /// Width of an [OnColorChip] (padding L9 R11, 16 px icon + 5 gap,
  /// 12/800 label) at the current text scale.
  static double _chipWidth(BuildContext context, String label, bool icon) {
    final painter = TextPainter(
      text: TextSpan(
        text: label,
        style: AppTypography.meta.copyWith(
          fontWeight: FontWeight.w800,
          letterSpacing: 0,
        ),
      ),
      textDirection: Directionality.of(context),
      textScaler: MediaQuery.textScalerOf(context),
      maxLines: 1,
    )..layout();
    final width = painter.width;
    painter.dispose();
    return 9 + (icon ? 16 + 5 : 0) + width + 11 + 1;
  }

  @override
  Widget build(BuildContext context) {
    final ranking = _RankingBadge.describe(rankingReason);
    final counter = '${index + 1} / $total';

    return Row(
      children: [
        Expanded(
          child: LayoutBuilder(
            builder: (context, constraints) {
              final needed = _chipWidth(context, ranking.$2, true) +
                  KararSpace.s8 +
                  _chipWidth(context, counter, false);
              final showCounter = needed <= constraints.maxWidth;
              return Row(
                children: [
                  Flexible(
                    child: _RankingBadge(reason: rankingReason, blur: onPhoto),
                  ),
                  if (showCounter) ...[
                    const SizedBox(width: KararSpace.s8),
                    OnColorChip(label: counter, blur: onPhoto),
                  ],
                ],
              );
            },
          ),
        ),
        const SizedBox(width: KararSpace.s8),
        OnColorChip(
          icon: Icons.visibility_off_rounded,
          label: 'İlgilenmiyorum',
          blur: onPhoto,
          onTap: onNotInterested,
        ),
        // 8 px visual gap (the button's 44 px hit box adds 7 px).
        const SizedBox(width: KararSpace.s8 - 7),
        // Same 30 px visual height as the chips beside it (one aligned
        // row); the hit area stays 44 × 44.
        KararCircleButton(
          icon: Icons.dashboard_rounded,
          tooltip: 'Bölümler',
          style: onPhoto ? KararCircleStyle.glass : KararCircleStyle.onColor,
          size: _controlHeight,
          iconSize: 17,
          onPressed: onOpenSections,
        ),
      ],
    );
  }
}

class _AuthorRow extends StatelessWidget {
  const _AuthorRow({required this.post});

  final Post post;

  @override
  Widget build(BuildContext context) {
    final onColor = context.kc.onColor;
    final name = post.authorName;
    final anonymous = post.isAnonymous || name == null || name.isEmpty;
    final handle = anonymous ? '@anonim' : '@$name';

    return ConstrainedBox(
      constraints: const BoxConstraints(minHeight: 32),
      child: Row(
        children: [
          KararAvatar(
            username: anonymous ? '' : name,
            radius: 14,
            anonymous: anonymous,
            onColor: true,
          ),
          const SizedBox(width: KararSpace.s8),
          Flexible(
            child: Text.rich(
              TextSpan(
                children: [
                  TextSpan(
                    text: handle,
                    style: AppTypography.bodyS.copyWith(
                      color: onColor,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  TextSpan(
                    text: ' · ${post.createdAgo}',
                    style: AppTypography.labelS.copyWith(
                      color: onColor,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ],
              ),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
          ),
        ],
      ),
    );
  }
}

/// Post body: 3 lines, tap to expand/collapse; "Devamını oku" when it is
/// actually truncated.
///
/// When truncated, the paragraph and the "Devamını oku" link form one
/// button (same action), so the link sits 4 px under the text instead of
/// inside its own 44 px hit box; the paragraph is far taller than 44 px.
class _ExpandableContent extends StatelessWidget {
  const _ExpandableContent({
    required this.text,
    required this.expanded,
    required this.onToggle,
  });

  final String text;
  final bool expanded;
  final VoidCallback onToggle;

  static const _collapsedLines = 3;

  @override
  Widget build(BuildContext context) {
    final onColor = context.kc.onColor;
    final style = AppTypography.body.copyWith(
      color: onColor,
      fontWeight: FontWeight.w500,
    );
    return LayoutBuilder(
      builder: (context, constraints) {
        final painter = TextPainter(
          text: TextSpan(text: text, style: style),
          textDirection: Directionality.of(context),
          textScaler: MediaQuery.textScalerOf(context),
          maxLines: _collapsedLines,
        )..layout(maxWidth: constraints.maxWidth);
        final truncates = painter.didExceedMaxLines;
        painter.dispose();

        final paragraph = Text(
          text,
          maxLines: expanded ? null : _collapsedLines,
          overflow: expanded ? TextOverflow.visible : TextOverflow.ellipsis,
          style: style,
        );

        if (expanded || !truncates) {
          return GestureDetector(onTap: onToggle, child: paragraph);
        }

        return KararPressable(
          onTap: onToggle,
          // The paragraph text and the link label stay readable as the
          // button's content.
          excludeChildSemantics: false,
          pressedScale: 0.99,
          focusRadius: KararRadius.xs,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              paragraph,
              const SizedBox(height: KararSpace.s4),
              Text(
                'Devamını oku',
                style: AppTypography.bodyS.copyWith(
                  color: onColor,
                  fontWeight: FontWeight.w800,
                  decoration: TextDecoration.underline,
                  decorationColor: onColor,
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}

/// Single-line hashtag row: up to three tags, never wrapping — tags that do
/// not fit are dropped. Each tag opens the search.
///
/// Every tag's hit box is [boxHeight] tall with the text centred; the text
/// block overlaps it by [overlapAbove] (owned by the text above) and
/// [overlapBelow] (over the non-interactive verdict chip), which leaves
/// each tag a full 44 px of its own hit height.
class _TagRow extends StatelessWidget {
  const _TagRow({required this.tags});

  final List<String> tags;

  static const _gap = 10.0;

  /// Hit-box height of each tag (text centred).
  static const boxHeight = 64.0;

  /// Strip at the top of the box covered by the text above.
  static const overlapAbove = 20.0;

  /// Strip at the bottom of the box drawn over the verdict chip: leaves a
  /// 12 px gap between the tag text (17 px line) and the chip.
  static const overlapBelow = (boxHeight - 17) / 2 - KararSpace.s12;

  @override
  Widget build(BuildContext context) {
    final style = AppTypography.labelS.copyWith(
      color: context.kc.onColor,
      fontWeight: FontWeight.w800,
    );
    final scaler = MediaQuery.textScalerOf(context);
    final direction = Directionality.of(context);

    return LayoutBuilder(
      builder: (context, constraints) {
        final fitted = <String>[];
        var used = 0.0;
        for (final tag in tags.take(3)) {
          final painter = TextPainter(
            text: TextSpan(text: '#$tag', style: style),
            textDirection: direction,
            textScaler: scaler,
            maxLines: 1,
          )..layout();
          final width = math.max(painter.width, KararSpace.minTap);
          painter.dispose();
          final next = used + (fitted.isEmpty ? 0 : _gap) + width;
          if (next > constraints.maxWidth) continue;
          used = next;
          fitted.add(tag);
        }
        // Nothing fits: keep a plain 12 px gap above the verdict chip
        // (the block subtracts both overlaps from this slot).
        if (fitted.isEmpty) {
          return const SizedBox(
            height: overlapAbove + overlapBelow + KararSpace.s12,
          );
        }

        return Row(
          children: [
            for (var i = 0; i < fitted.length; i++) ...[
              if (i > 0) const SizedBox(width: _gap),
              KararPressable(
                onTap: () => context.push(
                    '/search?q=${Uri.encodeComponent('#${fitted[i]}')}'),
                semanticLabel: '#${fitted[i]}',
                focusRadius: KararRadius.xs,
                minSize: const Size(KararSpace.minTap, boxHeight),
                child: ConstrainedBox(
                  constraints:
                      const BoxConstraints(minWidth: KararSpace.minTap),
                  child: Text(
                    '#${fitted[i]}',
                    maxLines: 1,
                    softWrap: false,
                    style: style,
                  ),
                ),
              ),
            ],
          ],
        );
      },
    );
  }
}

class _RankingBadge extends StatelessWidget {
  const _RankingBadge({required this.reason, this.blur = false});

  final String reason;
  final bool blur;

  /// Icon and label for a ranking reason.
  static (IconData, String) describe(String reason) => switch (reason) {
        'rising' => (Icons.trending_up_rounded, 'Yükseliyor'),
        'controversial' => (Icons.balance_rounded, 'Tartışmalı'),
        'fresh' => (Icons.fiber_new_rounded, 'Yeni'),
        'serendipity' => (Icons.shuffle_rounded, 'Farklı Bir Şey'),
        'ucb_explore' => (Icons.explore_rounded, 'Keşif'),
        _ => (Icons.local_fire_department_rounded, 'Trend'),
      };

  @override
  Widget build(BuildContext context) {
    final (icon, label) = describe(reason);
    return OnColorChip(icon: icon, label: label, blur: blur);
  }
}

// ─── Comments bottom sheet ───────────────────────────────────────────────────

class _DiscoverCommentsSheet extends ConsumerWidget {
  const _DiscoverCommentsSheet({
    required this.post,
    required this.impressionToken,
    required this.rankingReason,
  });

  final Post post;
  final String impressionToken;
  final String rankingReason;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final kc = context.kc;

    return DraggableScrollableSheet(
      initialChildSize: 0.7,
      minChildSize: 0.4,
      maxChildSize: 0.95,
      expand: false,
      builder: (context, scrollController) {
        return DecoratedBox(
          decoration: BoxDecoration(
            color: kc.sheet,
            borderRadius: const BorderRadius.vertical(
              top: Radius.circular(KararRadius.card),
            ),
          ),
          child: Column(
            children: [
              KararSheetHeader(
                title: 'Yorumlar',
                count: '${post.commentCount}',
                onClose: () => Navigator.of(context).pop(),
              ),
              Divider(height: 1, thickness: 1, color: kc.outlineVariant),

              // Comment list
              Expanded(
                child: _CommentsBody(
                  postId: post.id,
                  isPostOwner: post.isOwner,
                  scrollController: scrollController,
                  impressionToken: impressionToken,
                  rankingReason: rankingReason,
                ),
              ),

              // Comment input
              _DiscoverCommentInput(
                postId: post.id,
                impressionToken: impressionToken,
                rankingReason: rankingReason,
              ),
            ],
          ),
        );
      },
    );
  }
}

class _CommentsBody extends ConsumerWidget {
  const _CommentsBody({
    required this.postId,
    required this.isPostOwner,
    required this.scrollController,
    required this.impressionToken,
    required this.rankingReason,
  });

  final String postId;
  final bool isPostOwner;
  final ScrollController scrollController;
  final String impressionToken;
  final String rankingReason;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final detailState = ref.watch(postDetailProvider(postId));

    if (detailState.isLoadingComments && detailState.comments.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    if (detailState.error != null && detailState.comments.isEmpty) {
      return Center(
        child: ErrorView(
          message: detailState.error!,
          onRetry: () =>
              ref.read(postDetailProvider(postId).notifier).loadComments(),
        ),
      );
    }

    if (detailState.comments.isEmpty) {
      return const EmptyState(
        message: 'Henüz yorum yok. İlk yorumu sen yap.',
        icon: Icons.chat_bubble_outline_rounded,
      );
    }

    return CommentList(
      comments: detailState.comments,
      postId: postId,
      shrinkWrap: false,
      padding: const EdgeInsets.symmetric(horizontal: 12),
      isPostOwner: isPostOwner,
      onUpvote: (c) {
        ref.read(postDetailProvider(postId).notifier).upvoteComment(c);
        ref.read(postRepositoryProvider).sendDiscoverEvent(
              postId: postId,
              eventType: 'comment_like',
              impressionToken: impressionToken,
              rankingReason: rankingReason,
            );
      },
      onDownvote: (c) {
        ref.read(postDetailProvider(postId).notifier).downvoteComment(c);
        ref.read(postRepositoryProvider).sendDiscoverEvent(
              postId: postId,
              eventType: 'comment_dislike',
              impressionToken: impressionToken,
              rankingReason: rankingReason,
            );
      },
      onDelete: (c) =>
          ref.read(postDetailProvider(postId).notifier).deleteComment(c),
      onReply: (c) =>
          ref.read(postDetailProvider(postId).notifier).setReplyingTo(c),
    );
  }
}

/// Composer pinned under the comments sheet (spec §6.2 composer visuals).
class _DiscoverCommentInput extends ConsumerStatefulWidget {
  const _DiscoverCommentInput({
    required this.postId,
    required this.impressionToken,
    required this.rankingReason,
  });

  final String postId;
  final String impressionToken;
  final String rankingReason;

  @override
  ConsumerState<_DiscoverCommentInput> createState() =>
      _DiscoverCommentInputState();
}

class _DiscoverCommentInputState extends ConsumerState<_DiscoverCommentInput> {
  final _ctrl = TextEditingController();
  final _focusNode = FocusNode();
  var _loading = false;
  var _hasText = false;

  @override
  void initState() {
    super.initState();
    _ctrl.addListener(_onTextChanged);
  }

  void _onTextChanged() {
    final hasText = _ctrl.text.trim().isNotEmpty;
    if (hasText != _hasText) setState(() => _hasText = hasText);
  }

  @override
  void dispose() {
    _ctrl.removeListener(_onTextChanged);
    _ctrl.dispose();
    _focusNode.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final text = _ctrl.text.trim();
    if (text.isEmpty || _loading) return;
    final isReply =
        ref.read(postDetailProvider(widget.postId)).replyingToComment != null;
    setState(() => _loading = true);
    try {
      await ref
          .read(postDetailProvider(widget.postId).notifier)
          .submitComment(text);
      if (!mounted) return;
      ref.read(postRepositoryProvider).sendDiscoverEvent(
            postId: widget.postId,
            eventType: isReply ? 'comment_reply' : 'comment_open',
            impressionToken: widget.impressionToken,
            rankingReason: widget.rankingReason,
          );
      _ctrl.clear();
      ref.read(postDetailProvider(widget.postId).notifier).cancelReply();
    } catch (_) {
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final replyingTo = ref.watch(
      postDetailProvider(widget.postId).select((s) => s.replyingToComment),
    );

    ref.listen(
      postDetailProvider(widget.postId).select((s) => s.replyingToComment),
      (prev, next) {
        if (next != null && prev == null) _focusNode.requestFocus();
      },
    );

    OutlineInputBorder pill([BorderSide side = BorderSide.none]) =>
        OutlineInputBorder(
          borderRadius: KararRadius.all(KararRadius.surface),
          borderSide: side,
        );

    return DecoratedBox(
      decoration: BoxDecoration(
        color: kc.sheet,
        border: Border(top: BorderSide(color: kc.outlineVariant)),
      ),
      child: SafeArea(
        top: false,
        child: Padding(
          padding: EdgeInsets.fromLTRB(
            12,
            10,
            12,
            MediaQuery.viewInsetsOf(context).bottom + 12,
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (replyingTo != null)
                Container(
                  margin: const EdgeInsets.only(bottom: KararSpace.s8),
                  padding: const EdgeInsets.only(left: KararSpace.s12),
                  decoration: BoxDecoration(
                    color: kc.primaryContainer,
                    borderRadius: KararRadius.all(KararRadius.avatar),
                  ),
                  child: Row(
                    children: [
                      Icon(
                        Icons.reply_rounded,
                        size: 14,
                        color: kc.onPrimaryContainer,
                      ),
                      const SizedBox(width: KararSpace.s6),
                      Expanded(
                        child: Text(
                          '@${replyingTo.authorName ?? 'kullanıcı'} yanıtlanıyor',
                          style: AppTypography.meta.copyWith(
                            color: kc.onPrimaryContainer,
                            fontWeight: FontWeight.w700,
                          ),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      IconButton(
                        onPressed: () => ref
                            .read(postDetailProvider(widget.postId).notifier)
                            .cancelReply(),
                        tooltip: 'Yanıtlamayı iptal et',
                        icon: const Icon(Icons.close_rounded, size: 18),
                        color: kc.onPrimaryContainer,
                      ),
                    ],
                  ),
                ),
              Row(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Expanded(
                    child: TextField(
                      controller: _ctrl,
                      focusNode: _focusNode,
                      minLines: 1,
                      maxLines: 4,
                      textInputAction: TextInputAction.send,
                      textCapitalization: TextCapitalization.sentences,
                      onSubmitted: (_) => _submit(),
                      decoration: InputDecoration(
                        hintText:
                            replyingTo != null ? 'Yanıt yaz…' : 'Yorum yaz…',
                        filled: true,
                        fillColor: kc.muted,
                        constraints: const BoxConstraints(minHeight: 48),
                        border: pill(),
                        enabledBorder: pill(),
                        focusedBorder:
                            pill(BorderSide(color: kc.primary, width: 1.5)),
                        contentPadding: const EdgeInsets.symmetric(
                          horizontal: 18,
                          vertical: 13,
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(width: KararSpace.s10),
                  if (_loading)
                    SizedBox(
                      width: 44,
                      height: 48,
                      child: Center(
                        child: SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(
                            strokeWidth: 2,
                            color: kc.primary,
                          ),
                        ),
                      ),
                    )
                  else
                    Padding(
                      padding: const EdgeInsets.only(bottom: 2),
                      child: KararCircleButton(
                        icon: Icons.arrow_upward_rounded,
                        onPressed: _hasText ? _submit : null,
                        tooltip: replyingTo != null
                            ? 'Yanıt gönder'
                            : 'Yorum gönder',
                        style: KararCircleStyle.primary,
                        size: 44,
                      ),
                    ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

// ─── Sections view ────────────────────────────────────────────────────────────

class _DiscoverSectionsView extends ConsumerWidget {
  const _DiscoverSectionsView();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final kc = context.kc;
    final sectionsAsync = ref.watch(discoverProvider);
    return sectionsAsync.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (_, __) => Center(
        child: ErrorView(
          message: 'Bölümler yüklenemedi',
          onRetry: () => ref.invalidate(discoverProvider),
        ),
      ),
      data: (data) {
        final sections =
            <(String, IconData, Color, Color, String, List<Post>)>[
          (
            'Yükselenler',
            Icons.trending_up_rounded,
            kc.hakliSoft,
            kc.hakliText,
            'Hızla oy alıyor',
            data.rising,
          ),
          (
            'İkiye Bölenler',
            Icons.balance_rounded,
            kc.primaryContainer,
            kc.primaryText,
            'Topluluk ikiye bölündü',
            data.controversial,
          ),
          (
            'Yeni Karar Bekleyenler',
            Icons.fiber_new_rounded,
            kc.warnSoft,
            kc.warnText,
            'Az oylı, yeni tartışmalar',
            data.fresh,
          ),
          if (data.cityTrending.isNotEmpty && data.city != null)
            (
              '${data.city!}\'da Trend',
              Icons.location_on_rounded,
              kc.muted,
              kc.text1,
              'Şehrinde öne çıkıyor',
              data.cityTrending,
            ),
          if (data.serendipity.isNotEmpty)
            (
              'Farklı Bir Şey',
              Icons.shuffle_rounded,
              kc.primaryContainer,
              kc.onPrimaryContainer,
              'Henüz keşfetmediğin kategorilerden',
              data.serendipity,
            ),
        ];

        final allEmpty = sections.every((s) => s.$6.isEmpty);
        if (allEmpty) {
          return Center(
            child: EmptyState(
              message: 'Şu an bölümlerde içerik yok.\nBiraz sonra tekrar dene.',
              icon: Icons.dashboard_outlined,
              action: () => ref.invalidate(discoverProvider),
              actionLabel: 'Yenile',
            ),
          );
        }

        final items = <Widget>[];
        for (final (label, icon, background, foreground, subtitle, posts)
            in sections) {
          if (posts.isEmpty) continue;
          items.add(_SectionHeader(
            label: label,
            icon: icon,
            background: background,
            foreground: foreground,
            subtitle: subtitle,
          ));
          for (final post in posts.take(5)) {
            items.add(Padding(
              padding: const EdgeInsets.symmetric(
                horizontal: KararSpace.gutter,
                vertical: KararSpace.s6,
              ),
              // TODO(owner A): pass `variant: PostCardVariant.compact` (spec
              // §6.3 sections view) once the compact variant lands in
              // post_card.dart.
              child: PostCard(
                post: post,
                onTap: () => context.push('/posts/${post.id}', extra: post),
              ),
            ));
          }
        }

        return CenteredContent(
          child: ListView(
            padding: EdgeInsets.only(
              top: KararSpace.s4,
              bottom: context.navBottomInset + KararSpace.s24,
            ),
            children: items,
          ),
        );
      },
    );
  }
}

class _SectionHeader extends StatelessWidget {
  const _SectionHeader({
    required this.label,
    required this.icon,
    required this.background,
    required this.foreground,
    required this.subtitle,
  });

  final String label;
  final IconData icon;
  final Color background;
  final Color foreground;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Padding(
      padding: const EdgeInsets.fromLTRB(
        KararSpace.gutter,
        KararSpace.s24,
        KararSpace.gutter,
        KararSpace.s8,
      ),
      child: Row(
        children: [
          ExcludeSemantics(
            child: Container(
              width: 36,
              height: 36,
              decoration: BoxDecoration(
                color: background,
                borderRadius: KararRadius.all(KararRadius.sm),
              ),
              child: Icon(icon, size: 20, color: foreground),
            ),
          ),
          const SizedBox(width: KararSpace.s12),
          Expanded(
            child: Semantics(
              header: true,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    label,
                    style: AppTypography.tab.copyWith(color: kc.text1),
                  ),
                  const SizedBox(height: KararSpace.s2),
                  Text(
                    subtitle,
                    style: AppTypography.meta.copyWith(color: kc.text2),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

// ─── Loading skeleton ─────────────────────────────────────────────────────────

/// Full-bleed dark shimmer page: top chips, rail placeholders, text bars.
class _DiscoverLoadingSkeleton extends StatelessWidget {
  const _DiscoverLoadingSkeleton();

  @override
  Widget build(BuildContext context) {
    const dark = KararColors.dark;
    final bottomInset = context.navBottomInset;
    const rail = VoteRailMetrics.fullscreen;

    Widget bone(double height, double width, {double radius = 8}) =>
        Container(
          height: height,
          width: width,
          decoration: BoxDecoration(
            color: dark.surface,
            borderRadius: KararRadius.all(radius),
          ),
        );

    Widget bones = Stack(
      children: [
        Positioned(
          top: 0,
          left: 16,
          right: 16,
          child: SafeArea(
            bottom: false,
            child: SizedBox(
              height: 64,
              child: Row(
                children: [
                  bone(30, 104, radius: 15),
                  const SizedBox(width: KararSpace.s8),
                  bone(30, 56, radius: 15),
                  const Spacer(),
                  bone(30, 128, radius: 15),
                  const SizedBox(width: KararSpace.s8),
                  bone(30, 30, radius: 15),
                ],
              ),
            ),
          ),
        ),
        Positioned(
          left: KararSpace.s20,
          right: rail.width + 24,
          bottom: bottomInset + 20,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  bone(28, 28, radius: 14),
                  const SizedBox(width: KararSpace.s8),
                  bone(14, 120),
                ],
              ),
              const SizedBox(height: 14),
              bone(24, double.infinity),
              const SizedBox(height: KararSpace.s8),
              bone(24, 200),
              const SizedBox(height: 14),
              bone(14, double.infinity),
              const SizedBox(height: KararSpace.s6),
              bone(14, 220),
              const SizedBox(height: KararSpace.s16),
              bone(34, double.infinity, radius: 17),
            ],
          ),
        ),
        Positioned(
          right: (rail.width - rail.voteCapsule.width) / 2,
          bottom: bottomInset + 24 + 34,
          child: Column(
            children: [
              bone(rail.voteCapsule.height, rail.voteCapsule.width,
                  radius: rail.voteCapsule.width / 2),
              SizedBox(height: rail.gap),
              bone(rail.voteCapsule.height, rail.voteCapsule.width,
                  radius: rail.voteCapsule.width / 2),
              SizedBox(height: rail.dividerBlock),
              bone(rail.commentCapsule.height, rail.commentCapsule.width,
                  radius: rail.commentCapsule.width / 2),
              SizedBox(height: rail.gap),
              bone(rail.circle, rail.circle, radius: rail.circle / 2),
              SizedBox(height: rail.gap),
              bone(rail.circle, rail.circle, radius: rail.circle / 2),
            ],
          ),
        ),
      ],
    );

    if (!KararMotion.reduce(context)) {
      bones = Shimmer.fromColors(
        baseColor: dark.surface,
        highlightColor: dark.elevated,
        period: const Duration(milliseconds: 1400),
        child: bones,
      );
    }

    return ColoredBox(
      color: dark.canvas,
      child: SizedBox.expand(child: bones),
    );
  }
}
