import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/karar_logo.dart';
import '../../shared/widgets/skeleton.dart';
import '../post_detail/comment_list.dart';
import '../post_detail/post_detail_provider.dart';
import '../post_detail/vote_bar.dart';
import 'discover_feed_provider.dart';

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
  }

  void _handlePageLeave(int index, List<DiscoverFeedItem> items) {
    if (index >= items.length) return;
    final item = items[index];
    if (_activePostId != item.post.id) return;

    final dwell = DateTime.now().difference(_pageEnteredAt).inSeconds;
    final repo = ref.read(postRepositoryProvider);
    _activePostId = null;

    if (dwell >= 3) {
      repo.sendDiscoverEvent(
        postId: item.post.id,
        eventType: 'dwell',
        dwellSeconds: dwell,
        impressionToken: item.impressionToken,
      );
    } else {
      repo.sendDiscoverEvent(
        postId: item.post.id,
        eventType: 'skip',
        impressionToken: item.impressionToken,
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

    return Scaffold(
      backgroundColor: Theme.of(context).colorScheme.surface,
      extendBodyBehindAppBar: true,
      appBar: AppBar(
        backgroundColor: Colors.transparent,
        elevation: 0,
        leadingWidth: 160,
        leading: InkWell(
          onTap: () => context.go('/'),
          child: const Padding(
            padding: EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: KararLogo(size: LogoSize.medium),
          ),
        ),
        title: const Text(
          'Keşfet',
          style: TextStyle(fontWeight: FontWeight.bold),
        ),
        centerTitle: true,
      ),
      body: feedAsync.when(
        data: (state) {
          if (state.items.isEmpty) {
            return Center(
              child: EmptyState(
                message:
                    'Şu an keşfedilecek içerik yok.\nBiraz sonra tekrar dene.',
                icon: Icons.explore_outlined,
                action: () => ref.invalidate(discoverFeedProvider),
                actionLabel: 'Yenile',
              ),
            );
          }
          return PageView.builder(
            controller: _ctrl,
            scrollDirection: Axis.vertical,
            itemCount: state.items.length + (state.isLoadingMore ? 1 : 0),
            itemBuilder: (context, index) {
              if (index >= state.items.length) {
                return const Center(child: CircularProgressIndicator());
              }
              final item = state.items[index];
              return _DiscoverCard(
                key: ValueKey(item.post.id),
                item: item,
                index: index,
                total: state.items.length,
                onVote: (voteType) => ref
                    .read(discoverFeedProvider.notifier)
                    .vote(item.post.id, voteType,
                        impressionToken: item.impressionToken),
                onNotInterested: () {
                  ref
                      .read(discoverFeedProvider.notifier)
                      .removeItem(item.post.id);
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
                      );
                  ref
                      .read(postRepositoryProvider)
                      .markNotInterested(item.post.id);
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
                      );
                  _showCommentsSheet(context, item.post);
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
                        );
                  }
                },
              );
            },
          );
        },
        loading: () => const _DiscoverLoadingSkeleton(),
        error: (error, _) => Center(
          child: ErrorView(
            message: 'Keşfet yüklenemedi',
            onRetry: () => ref.invalidate(discoverFeedProvider),
          ),
        ),
      ),
    );
  }

  void _showCommentsSheet(BuildContext context, Post post) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => _DiscoverCommentsSheet(post: post),
    );
  }
}

class _DiscoverCard extends ConsumerStatefulWidget {
  const _DiscoverCard({
    super.key,
    required this.item,
    required this.index,
    required this.total,
    required this.onVote,
    required this.onNotInterested,
    required this.onCommentOpen,
    required this.onSave,
  });

  final DiscoverFeedItem item;
  final int index;
  final int total;
  final Future<void> Function(VoteType voteType) onVote;
  final VoidCallback onNotInterested;
  final VoidCallback onCommentOpen;
  final Future<void> Function(bool isSaved) onSave;

  @override
  ConsumerState<_DiscoverCard> createState() => _DiscoverCardState();
}

class _DiscoverCardState extends ConsumerState<_DiscoverCard> {
  bool _isSaved = false;
  bool _isVoting = false;
  bool _expanded = false;

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
    setState(() => _isVoting = true);
    await widget.onVote(type);
    if (mounted) setState(() => _isVoting = false);
  }

  @override
  Widget build(BuildContext context) {
    final post = widget.item.post;
    final theme = Theme.of(context);
    final isDark = theme.brightness == Brightness.dark;

    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 56, 12, 8),
        child: Card(
          elevation: 0,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(20),
            side: BorderSide(
              color: theme.dividerColor.withValues(alpha: 0.08),
            ),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              // Progress indicator
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 14, 16, 0),
                child: Row(
                  children: [
                    _RankingBadge(reason: widget.item.rankingReason),
                    const Spacer(),
                    Text(
                      '${widget.index + 1} / ${widget.total}',
                      style: TextStyle(
                        fontSize: 11,
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),

              // Scrollable content area
              Expanded(
                child: SingleChildScrollView(
                  padding: const EdgeInsets.fromLTRB(16, 10, 16, 8),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      // Category chip
                      Container(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 10, vertical: 4),
                        decoration: BoxDecoration(
                          color: AppColors.primary.withValues(alpha: 0.1),
                          borderRadius: BorderRadius.circular(20),
                        ),
                        child: Text(
                          '${post.category.icon} ${post.category.name}',
                          style: const TextStyle(
                            fontSize: 12,
                            color: AppColors.primary,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                      const SizedBox(height: 12),

                      // Title
                      GestureDetector(
                        onTap: () =>
                            context.push('/posts/${post.id}', extra: post),
                        child: Text(
                          post.title,
                          style: theme.textTheme.titleLarge?.copyWith(
                            fontWeight: FontWeight.bold,
                            height: 1.3,
                          ),
                        ),
                      ),
                      const SizedBox(height: 10),

                      // Content (expandable)
                      if (post.content.isNotEmpty) ...[
                        GestureDetector(
                          onTap: () => setState(() => _expanded = !_expanded),
                          child: Text(
                            post.content,
                            maxLines: _expanded ? null : 5,
                            overflow: _expanded
                                ? TextOverflow.visible
                                : TextOverflow.ellipsis,
                            style: theme.textTheme.bodyMedium?.copyWith(
                              color: theme.colorScheme.onSurfaceVariant,
                              height: 1.5,
                            ),
                          ),
                        ),
                        if (!_expanded && post.content.length > 200)
                          GestureDetector(
                            onTap: () => setState(() => _expanded = true),
                            child: const Text(
                              'Devamını oku',
                              style: TextStyle(
                                color: AppColors.primary,
                                fontSize: 13,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ),
                        const SizedBox(height: 10),
                      ],

                      // Image
                      if (post.imageUrl != null) ...[
                        ClipRRect(
                          borderRadius: BorderRadius.circular(12),
                          child: Image.network(
                            post.imageUrl!,
                            width: double.infinity,
                            height: 200,
                            fit: BoxFit.cover,
                            errorBuilder: (_, __, ___) =>
                                const SizedBox.shrink(),
                          ),
                        ),
                        const SizedBox(height: 10),
                      ],

                      // Author + time
                      Row(
                        children: [
                          Icon(
                            Icons.person_outline,
                            size: 14,
                            color: theme.colorScheme.onSurfaceVariant,
                          ),
                          const SizedBox(width: 4),
                          Text(
                            post.isAnonymous
                                ? 'Anonim'
                                : (post.authorName ?? 'Anonim'),
                            style: TextStyle(
                              fontSize: 12,
                              color: theme.colorScheme.onSurfaceVariant,
                            ),
                          ),
                          const SizedBox(width: 6),
                          Text(
                            '·',
                            style: TextStyle(
                              color: theme.colorScheme.onSurfaceVariant,
                            ),
                          ),
                          const SizedBox(width: 6),
                          Text(
                            post.createdAgo,
                            style: TextStyle(
                              fontSize: 12,
                              color: theme.colorScheme.onSurfaceVariant,
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),

              // Bottom section: vote bar + buttons
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 4, 16, 12),
                child: Column(
                  children: [
                    // VoteBar (visual)
                    VoteBar(post: post, isCompact: true),
                    const SizedBox(height: 8),

                    // Vote buttons
                    Row(
                      children: [
                        Expanded(
                          child: _VoteButton(
                            label: 'Haklı',
                            icon: Icons.check_rounded,
                            color:
                                isDark ? AppColors.darkHakli : AppColors.hakli,
                            isSelected: post.myVote == VoteType.hakli,
                            isLoading: _isVoting,
                            onTap: () => _handleVote(VoteType.hakli),
                          ),
                        ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: _VoteButton(
                            label: 'Haksız',
                            icon: Icons.close_rounded,
                            color: isDark
                                ? AppColors.darkHaksiz
                                : AppColors.haksiz,
                            isSelected: post.myVote == VoteType.haksiz,
                            isLoading: _isVoting,
                            onTap: () => _handleVote(VoteType.haksiz),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 10),

                    // Action row
                    Row(
                      children: [
                        // Comments
                        _ActionButton(
                          icon: Icons.chat_bubble_outline_rounded,
                          label: '${post.commentCount}',
                          onTap: widget.onCommentOpen,
                        ),
                        const SizedBox(width: 4),
                        // Save
                        _ActionButton(
                          icon: _isSaved
                              ? Icons.bookmark_rounded
                              : Icons.bookmark_outline_rounded,
                          label: '',
                          color: _isSaved ? AppColors.primary : null,
                          onTap: () async {
                            final prev = _isSaved;
                            setState(() => _isSaved = !_isSaved);
                            try {
                              await widget.onSave(prev);
                            } catch (_) {
                              if (mounted) setState(() => _isSaved = prev);
                            }
                          },
                        ),
                        const SizedBox(width: 4),
                        // Open full post
                        _ActionButton(
                          icon: Icons.open_in_new_rounded,
                          label: '',
                          onTap: () =>
                              context.push('/posts/${post.id}', extra: post),
                        ),
                        const Spacer(),
                        // Not interested
                        TextButton(
                          onPressed: widget.onNotInterested,
                          style: TextButton.styleFrom(
                            padding: const EdgeInsets.symmetric(
                                horizontal: 10, vertical: 4),
                            minimumSize: Size.zero,
                            tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                          ),
                          child: Text(
                            'İlgilenmiyorum',
                            style: TextStyle(
                              fontSize: 12,
                              color: theme.colorScheme.onSurfaceVariant,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _VoteButton extends StatelessWidget {
  const _VoteButton({
    required this.label,
    required this.icon,
    required this.color,
    required this.isSelected,
    required this.isLoading,
    required this.onTap,
  });

  final String label;
  final IconData icon;
  final Color color;
  final bool isSelected;
  final bool isLoading;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return AnimatedContainer(
      duration: const Duration(milliseconds: 200),
      decoration: BoxDecoration(
        color: isSelected ? color : color.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(
          color: isSelected ? color : color.withValues(alpha: 0.3),
          width: isSelected ? 1.5 : 1,
        ),
      ),
      child: InkWell(
        onTap: isLoading ? null : onTap,
        borderRadius: BorderRadius.circular(10),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 10),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              if (isLoading)
                SizedBox(
                  width: 14,
                  height: 14,
                  child: CircularProgressIndicator(
                    strokeWidth: 2,
                    color: isSelected ? Colors.white : color,
                  ),
                )
              else
                Icon(
                  icon,
                  size: 16,
                  color: isSelected ? Colors.white : color,
                ),
              const SizedBox(width: 6),
              Text(
                label,
                style: TextStyle(
                  fontWeight: FontWeight.w700,
                  fontSize: 13,
                  color: isSelected ? Colors.white : color,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _ActionButton extends StatelessWidget {
  const _ActionButton({
    required this.icon,
    required this.label,
    required this.onTap,
    this.color,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final iconColor = color ?? theme.colorScheme.onSurfaceVariant;
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(8),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 20, color: iconColor),
            if (label.isNotEmpty) ...[
              const SizedBox(width: 4),
              Text(
                label,
                style: TextStyle(
                  fontSize: 13,
                  color: iconColor,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _RankingBadge extends StatelessWidget {
  const _RankingBadge({required this.reason});

  final String reason;

  @override
  Widget build(BuildContext context) {
    final (icon, label, color) = switch (reason) {
      'rising' => (Icons.trending_up_rounded, 'Yükseliyor', AppColors.hakli),
      'controversial' => (
          Icons.balance_rounded,
          'Tartışmalı',
          AppColors.primary
        ),
      'fresh' => (Icons.fiber_new_rounded, 'Yeni', AppColors.haksiz),
      _ => (Icons.local_fire_department_rounded, 'Trend', Colors.orange),
    };

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 12, color: color),
          const SizedBox(width: 4),
          Text(
            label,
            style: TextStyle(
              fontSize: 11,
              color: color,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

// ─── Comments bottom sheet ───────────────────────────────────────────────────

class _DiscoverCommentsSheet extends ConsumerWidget {
  const _DiscoverCommentsSheet({required this.post});

  final Post post;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);

    return DraggableScrollableSheet(
      initialChildSize: 0.7,
      minChildSize: 0.4,
      maxChildSize: 0.95,
      expand: false,
      builder: (context, scrollController) {
        return Container(
          decoration: BoxDecoration(
            color: theme.colorScheme.surface,
            borderRadius: const BorderRadius.vertical(top: Radius.circular(20)),
          ),
          child: Column(
            children: [
              // Handle
              const SizedBox(height: 12),
              Center(
                child: Container(
                  width: 40,
                  height: 4,
                  decoration: BoxDecoration(
                    color: theme.dividerColor,
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
              ),
              const SizedBox(height: 12),

              // Header
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                child: Row(
                  children: [
                    Text(
                      'Yorumlar',
                      style: theme.textTheme.titleMedium?.copyWith(
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                    const SizedBox(width: 6),
                    Text(
                      '(${post.commentCount})',
                      style: TextStyle(
                        color: theme.colorScheme.onSurfaceVariant,
                        fontSize: 14,
                      ),
                    ),
                  ],
                ),
              ),
              const Divider(height: 16),

              // Comment list
              Expanded(
                child: _CommentsBody(
                  postId: post.id,
                  isPostOwner: post.isOwner,
                  scrollController: scrollController,
                ),
              ),

              // Comment input
              _DiscoverCommentInput(postId: post.id),
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
  });

  final String postId;
  final bool isPostOwner;
  final ScrollController scrollController;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final detailState = ref.watch(postDetailProvider(postId));

    if (detailState.isLoadingComments && detailState.comments.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    if (detailState.comments.isEmpty) {
      return const EmptyState(
        message: 'Henüz yorum yok. İlk yorumu sen yap.',
        icon: Icons.chat_bubble_outline,
      );
    }

    return CommentList(
      comments: detailState.comments,
      postId: postId,
      shrinkWrap: false,
      padding: const EdgeInsets.symmetric(horizontal: 12),
      isPostOwner: isPostOwner,
      onUpvote: (c) =>
          ref.read(postDetailProvider(postId).notifier).upvoteComment(c),
      onDownvote: (c) =>
          ref.read(postDetailProvider(postId).notifier).downvoteComment(c),
      onDelete: (c) =>
          ref.read(postDetailProvider(postId).notifier).deleteComment(c),
    );
  }
}

class _DiscoverCommentInput extends ConsumerStatefulWidget {
  const _DiscoverCommentInput({required this.postId});

  final String postId;

  @override
  ConsumerState<_DiscoverCommentInput> createState() =>
      _DiscoverCommentInputState();
}

class _DiscoverCommentInputState extends ConsumerState<_DiscoverCommentInput> {
  final _ctrl = TextEditingController();
  var _loading = false;

  @override
  void dispose() {
    _ctrl.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final text = _ctrl.text.trim();
    if (text.isEmpty || _loading) return;
    setState(() => _loading = true);
    try {
      await ref
          .read(postDetailProvider(widget.postId).notifier)
          .submitComment(text);
      _ctrl.clear();
    } catch (_) {
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return SafeArea(
      child: Padding(
        padding: EdgeInsets.only(
          left: 12,
          right: 12,
          bottom: MediaQuery.of(context).viewInsets.bottom + 8,
          top: 8,
        ),
        child: Row(
          children: [
            Expanded(
              child: TextField(
                controller: _ctrl,
                maxLines: null,
                textInputAction: TextInputAction.send,
                onSubmitted: (_) => _submit(),
                decoration: InputDecoration(
                  hintText: 'Yorum yaz…',
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(20),
                    borderSide: BorderSide.none,
                  ),
                  filled: true,
                  fillColor: theme.colorScheme.surfaceContainerHighest
                      .withValues(alpha: 0.5),
                  contentPadding:
                      const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                ),
              ),
            ),
            const SizedBox(width: 8),
            _loading
                ? const SizedBox(
                    width: 24,
                    height: 24,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : IconButton(
                    onPressed: _submit,
                    icon: const Icon(Icons.send_rounded),
                    color: AppColors.primary,
                  ),
          ],
        ),
      ),
    );
  }
}

// ─── Loading skeleton ─────────────────────────────────────────────────────────

class _DiscoverLoadingSkeleton extends StatelessWidget {
  const _DiscoverLoadingSkeleton();

  @override
  Widget build(BuildContext context) {
    return const SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(12, 56, 12, 8),
        child: Card(
          elevation: 0,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.all(Radius.circular(20)),
          ),
          child: Padding(
            padding: EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Skeleton(height: 20, width: 80, borderRadius: 12),
                SizedBox(height: 16),
                Skeleton(height: 24, width: double.infinity),
                SizedBox(height: 8),
                Skeleton(height: 24, width: 200),
                SizedBox(height: 16),
                Skeleton(height: 14, width: double.infinity),
                SizedBox(height: 6),
                Skeleton(height: 14, width: double.infinity),
                SizedBox(height: 6),
                Skeleton(height: 14, width: 180),
                Spacer(),
                Skeleton(height: 48, width: double.infinity, borderRadius: 10),
                SizedBox(height: 8),
                Row(
                  children: [
                    Expanded(child: Skeleton(height: 42, borderRadius: 10)),
                    SizedBox(width: 8),
                    Expanded(child: Skeleton(height: 42, borderRadius: 10)),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
