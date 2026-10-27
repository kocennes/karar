import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/karar_pressable.dart';
import '../../shared/widgets/karar_surface.dart';
import '../../shared/widgets/karar_text_tabs.dart';
import '../../shared/widgets/login_nudge.dart';
import 'categories_provider.dart';
import 'category_feed_provider.dart';
import 'post_card.dart';
import 'widgets/feed_skeletons.dart';
import 'widgets/horizontal_bleed_clip.dart';

class CategoryScreen extends ConsumerWidget {
  const CategoryScreen({super.key, required this.categoryId});

  final int categoryId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final categoriesAsync = ref.watch(categoriesProvider);

    final category = categoriesAsync.maybeWhen(
      data: (list) => list.where((c) => c.id == categoryId).firstOrNull,
      orElse: () => null,
    );

    return Scaffold(
      appBar: AppBar(
        // Category.icon holds a short word ("Aşk", "İş") — never shown.
        title: Text(
          category?.name ?? 'Kategori',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: _CategoryFeedList(
          categoryId: categoryId,
          header: _CategoryHeader(categoryId: categoryId, category: category),
        ),
      ),
    );
  }
}

/// Category header + sort tabs. The header is a surface card (56 px
/// gradient tile, name, follow / mute pills) rather than a second gradient
/// slab, so it reads as the page's header and not as another post card.
class _CategoryHeader extends StatelessWidget {
  const _CategoryHeader({required this.categoryId, required this.category});

  final int categoryId;
  final Category? category;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final brightness = Theme.of(context).brightness;
    final style = category != null
        ? CategoryStyle.of(category!)
        : CategoryStyle.resolve(id: categoryId);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
          child: KararSurface(
            radius: KararRadius.surface,
            padding: const EdgeInsets.all(16),
            child: Row(
              children: [
                ExcludeSemantics(
                  child: Container(
                    width: 56,
                    height: 56,
                    decoration: BoxDecoration(
                      gradient: style.gradientFor(brightness: brightness),
                      borderRadius: KararRadius.all(KararRadius.navItem),
                    ),
                    alignment: Alignment.center,
                    child: Icon(style.icon, size: 28, color: kc.onColor),
                  ),
                ),
                const SizedBox(width: 16),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Semantics(
                        header: true,
                        child: Text(
                          category?.name ?? 'Kategori',
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: AppTypography.screenTitle.copyWith(
                            color: kc.text1,
                          ),
                        ),
                      ),
                      const SizedBox(height: 8),
                      Wrap(
                        spacing: 8,
                        runSpacing: 0,
                        children: [
                          _FollowButton(categoryId: categoryId),
                          _MuteButton(categoryId: categoryId),
                        ],
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
        _SortBar(categoryId: categoryId),
      ],
    );
  }
}

enum _HeaderPillTone {
  /// Brand CTA: primaryFill + white.
  primary,

  /// Selected / on state: primaryContainer + onPrimaryContainer.
  tonal,

  /// Secondary action: muted + text2.
  muted,
}

/// 40 px pill on the header card (44 px hit box).
class _HeaderPill extends StatelessWidget {
  const _HeaderPill({
    required this.icon,
    required this.label,
    required this.onTap,
    required this.tone,
    this.selected,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final _HeaderPillTone tone;
  final bool? selected;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final (bg, fg, iconColor) = switch (tone) {
      _HeaderPillTone.primary => (kc.primaryFill, kc.onColor, kc.onColor),
      _HeaderPillTone.tonal => (
          kc.primaryContainer,
          kc.onPrimaryContainer,
          kc.primary,
        ),
      _HeaderPillTone.muted => (kc.muted, kc.text2, kc.text2),
    };
    return KararPressable(
      onTap: onTap,
      semanticLabel: label,
      selected: selected,
      focusRadius: 20,
      child: AnimatedContainer(
        duration: KararMotion.d(context, KararMotion.fast),
        curve: KararMotion.standard,
        constraints: const BoxConstraints(minHeight: 40),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
        decoration: BoxDecoration(
          color: bg,
          borderRadius: KararRadius.all(20),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 18, color: iconColor),
            const SizedBox(width: 6),
            Flexible(
              child: Text(
                label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: AppTypography.label.copyWith(color: fg),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _FollowButton extends ConsumerWidget {
  const _FollowButton({required this.categoryId});

  final int categoryId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final followedCategories = ref.watch(followedCategoriesProvider);
    final isFollowed = followedCategories.contains(categoryId);

    return _HeaderPill(
      tone: isFollowed ? _HeaderPillTone.tonal : _HeaderPillTone.primary,
      onTap: () {
        final user = ref.read(currentUserProvider);
        if (user == null) {
          LoginNudge.show(
            context,
            title: 'Kategoriyi takip et',
            message: 'Kategori güncellemelerini görmek için giriş yap.',
            returnTo: '/categories/$categoryId',
          );
          return;
        }
        ref.read(followedCategoriesProvider.notifier).toggle(categoryId);
        final nowFollowing = !isFollowed;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
                nowFollowing ? 'Kategori takip edildi' : 'Takip bırakıldı'),
            duration: const Duration(seconds: 2),
          ),
        );
      },
      icon: isFollowed ? Icons.star_rounded : Icons.star_border_rounded,
      label: isFollowed ? 'Takip ediliyor' : 'Takip Et',
      selected: isFollowed,
    );
  }
}

class _MuteButton extends ConsumerWidget {
  const _MuteButton({required this.categoryId});

  final int categoryId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final mutedCategories = ref.watch(mutedCategoriesProvider);
    final isMuted = mutedCategories.contains(categoryId);

    return _HeaderPill(
      tone: _HeaderPillTone.muted,
      onTap: () {
        final user = ref.read(currentUserProvider);
        if (user == null) {
          LoginNudge.show(
            context,
            title: 'Kategoriyi sessize al',
            message: 'Bu kategoriyi ana akışınızda gizlemek için giriş yapın.',
            returnTo: '/categories/$categoryId',
          );
          return;
        }
        ref.read(mutedCategoriesProvider.notifier).toggle(categoryId);
        final nowMuted = !isMuted;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
                nowMuted ? 'Kategori sessize alındı.' : 'Ses açıldı.'),
            duration: const Duration(seconds: 2),
          ),
        );
      },
      icon: isMuted ? Icons.volume_up_rounded : Icons.volume_off_rounded,
      label: isMuted ? 'Sesi Aç' : 'Sessize Al',
      selected: isMuted,
    );
  }
}

class _SortBar extends ConsumerWidget {
  const _SortBar({required this.categoryId});

  final int categoryId;

  static const _values = ['trending', 'new'];

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final sort =
        ref.watch(categoryFeedProvider(categoryId).select((s) => s.sort));
    final index = sort == 'new' ? 1 : 0;

    return Align(
      alignment: Alignment.centerLeft,
      child: KararTextTabs(
        labels: const ['Trend', 'Yeni'],
        index: index,
        padding: const EdgeInsets.fromLTRB(20, 8, 20, 4),
        onChanged: (i) => ref
            .read(categoryFeedProvider(categoryId).notifier)
            .changeSort(_values[i]),
      ),
    );
  }
}

class _CategoryFeedList extends ConsumerStatefulWidget {
  const _CategoryFeedList({required this.categoryId, required this.header});

  final int categoryId;

  /// Banner + tabs, scrolled with the list.
  final Widget header;

  @override
  ConsumerState<_CategoryFeedList> createState() => _CategoryFeedListState();
}

class _CategoryFeedListState extends ConsumerState<_CategoryFeedList> {
  final _scrollController = ScrollController();

  @override
  void initState() {
    super.initState();
    _scrollController.addListener(_onScroll);
  }

  @override
  void dispose() {
    _scrollController.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (_scrollController.position.pixels >=
        _scrollController.position.maxScrollExtent - 300) {
      ref.read(categoryFeedProvider(widget.categoryId).notifier).loadMore();
    }
  }

  @override
  Widget build(BuildContext context) {
    final feedState = ref.watch(categoryFeedProvider(widget.categoryId));
    final bottomPadding = context.navBottomInset + 24;

    if (feedState.isLoading) {
      return ListView(
        padding: EdgeInsets.only(bottom: bottomPadding),
        children: [
          widget.header,
          const SizedBox(height: 8),
          for (var i = 0; i < 2; i++) ...[
            if (i > 0) const SizedBox(height: KararSpace.feedGap),
            const Padding(
              padding: EdgeInsets.symmetric(horizontal: KararSpace.gutter),
              child: FeedCardSkeleton(),
            ),
          ],
        ],
      );
    }

    if (feedState.error != null && feedState.posts.isEmpty) {
      return ListView(
        padding: EdgeInsets.only(bottom: bottomPadding),
        children: [
          widget.header,
          SizedBox(
            height: 320,
            child: ErrorView(
              message: 'Gönderiler yüklenemedi',
              onRetry: () => ref
                  .read(categoryFeedProvider(widget.categoryId).notifier)
                  .refresh(),
            ),
          ),
        ],
      );
    }

    if (feedState.posts.isEmpty) {
      return ListView(
        padding: EdgeInsets.only(bottom: bottomPadding),
        children: [
          widget.header,
          const SizedBox(
            height: 320,
            child: EmptyState(
              icon: Icons.inbox_outlined,
              message: 'Bu kategoride henüz paylaşım yapılmamış.',
            ),
          ),
        ],
      );
    }

    // Card shadows may bleed sideways past the 680 px column; the list is
    // still cut at its top and bottom edges.
    return HorizontalBleedClip(
      child: RefreshIndicator(
        onRefresh: () => ref
            .read(categoryFeedProvider(widget.categoryId).notifier)
            .refresh(),
        child: BackdropGroup(
          child: ListView.separated(
            clipBehavior: Clip.none,
            controller: _scrollController,
            padding: EdgeInsets.only(bottom: bottomPadding),
            itemCount:
                1 + feedState.posts.length + (feedState.isLoadingMore ? 1 : 0),
            separatorBuilder: (_, index) =>
                SizedBox(height: index == 0 ? 8 : KararSpace.feedGap),
            itemBuilder: (context, index) {
              if (index == 0) return widget.header;
              final i = index - 1;
              if (i == feedState.posts.length) {
                return const Padding(
                  padding: EdgeInsets.symmetric(vertical: 16),
                  child: Center(
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                );
              }
              final post = feedState.posts[i];
              return Padding(
                padding:
                    const EdgeInsets.symmetric(horizontal: KararSpace.gutter),
                // Every card here is in this category: no category chip.
                child: PostCard(
                  post: post,
                  showCategory: false,
                  onTap: () => context.push('/posts/${post.id}', extra: post),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
