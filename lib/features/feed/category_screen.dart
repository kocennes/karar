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
import '../../shared/widgets/karar_text_tabs.dart';
import '../../shared/widgets/login_nudge.dart';
import '../../shared/widgets/post_surface.dart';
import 'categories_provider.dart';
import 'category_feed_provider.dart';
import 'post_card.dart';
import 'widgets/feed_skeletons.dart';
import 'widgets/horizontal_bleed_clip.dart';

class CategoryScreen extends ConsumerStatefulWidget {
  const CategoryScreen({super.key, required this.categoryId});

  final int categoryId;

  @override
  ConsumerState<CategoryScreen> createState() => _CategoryScreenState();
}

class _CategoryScreenState extends ConsumerState<CategoryScreen> {
  /// The banner carries the category name, so the app-bar title only fades
  /// in once the banner's title has scrolled under the bar (the page no
  /// longer opens with the name twice).
  final _showBarTitle = ValueNotifier<bool>(false);

  /// Scroll offset at which the banner's title has left the viewport.
  static const _barTitleOffset = 112.0;

  @override
  void dispose() {
    _showBarTitle.dispose();
    super.dispose();
  }

  bool _onScroll(ScrollNotification notification) {
    if (notification.depth != 0 ||
        notification.metrics.axis != Axis.vertical) {
      return false;
    }
    final show = notification.metrics.pixels > _barTitleOffset;
    if (show != _showBarTitle.value) _showBarTitle.value = show;
    return false;
  }

  @override
  Widget build(BuildContext context) {
    final categoryId = widget.categoryId;
    final categoriesAsync = ref.watch(categoriesProvider);

    final category = categoriesAsync.maybeWhen(
      data: (list) => list.where((c) => c.id == categoryId).firstOrNull,
      orElse: () => null,
    );

    return Scaffold(
      appBar: AppBar(
        // Category.icon holds a short word ("Aşk", "İş") — never shown.
        title: ValueListenableBuilder<bool>(
          valueListenable: _showBarTitle,
          builder: (context, show, child) => AnimatedOpacity(
            opacity: show ? 1 : 0,
            duration: KararMotion.fade(context, KararMotion.fast),
            curve: KararMotion.standard,
            child: child,
          ),
          child: Text(
            category?.name ?? 'Kategori',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
        ),
        centerTitle: true,
      ),
      body: NotificationListener<ScrollNotification>(
        onNotification: _onScroll,
        child: CenteredContent(
          child: _CategoryFeedList(
            categoryId: categoryId,
            header: _CategoryHeader(categoryId: categoryId, category: category),
          ),
        ),
      ),
    );
  }
}

/// Category banner + sort tabs (spec §5.10).
class _CategoryHeader extends StatelessWidget {
  const _CategoryHeader({required this.categoryId, required this.category});

  final int categoryId;
  final Category? category;

  @override
  Widget build(BuildContext context) {
    final style = category != null
        ? CategoryStyle.of(category!)
        : CategoryStyle.resolve(id: categoryId);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
          child: _CategoryBanner(
            categoryId: categoryId,
            name: category?.name ?? 'Kategori',
            style: style,
          ),
        ),
        _SortBar(categoryId: categoryId),
      ],
    );
  }
}

/// The category's gradient banner (spec §5.10): the palette's gradient and
/// sheen (light: coloured glow; dark: the dark stops + hairline), radius
/// 30, min height 150, padding 20; a 48 px white-20 % disc with the
/// category icon, the name in `titleXL` white and the follow / mute pills.
/// A large white-10 % category glyph bleeds off the bottom-right corner so
/// the wide (desktop) banner never reads as an empty slab. ≥ 560 px: the
/// pills sit right-aligned on the name's row.
///
/// It differs from a post card on purpose: 150 px instead of 440, no vote
/// rail, no quote watermark, the icon disc and the pills.
class _CategoryBanner extends StatelessWidget {
  const _CategoryBanner({
    required this.categoryId,
    required this.name,
    required this.style,
  });

  final int categoryId;
  final String name;
  final CategoryStyle style;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final disc = ExcludeSemantics(
      child: Container(
        width: 48,
        height: 48,
        decoration: BoxDecoration(
          color: kc.onColor.withValues(alpha: 0.20),
          shape: BoxShape.circle,
        ),
        alignment: Alignment.center,
        child: Icon(style.icon, size: 26, color: kc.onColor),
      ),
    );
    final title = Semantics(
      header: true,
      child: Text(
        name,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
        style: AppTypography.titleXL.copyWith(color: kc.onColor),
      ),
    );
    final pills = Wrap(
      spacing: 8,
      runSpacing: 4,
      children: [
        _FollowButton(categoryId: categoryId),
        _MuteButton(categoryId: categoryId),
      ],
    );

    return LayoutBuilder(
      builder: (context, constraints) {
        final wide = constraints.maxWidth >= 560;
        // The pills' 44 px hit boxes add 2 px above/below their 40 px
        // visual, hence the 10 / 18 gaps around them.
        final body = wide
            ? Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Align(alignment: Alignment.centerLeft, child: disc),
                  const SizedBox(height: 14),
                  Row(
                    children: [
                      Expanded(child: title),
                      const SizedBox(width: 16),
                      pills,
                    ],
                  ),
                ],
              )
            : Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  disc,
                  const SizedBox(height: 12),
                  title,
                  const SizedBox(height: 10),
                  pills,
                ],
              );

        return PostSurface.tinted(
          style: style,
          radius: KararRadius.card,
          showQuote: false,
          showRailSheen: false,
          child: Stack(
            children: [
              Positioned(
                right: -24,
                bottom: -40,
                child: ExcludeSemantics(
                  child: Icon(
                    style.icon,
                    size: 176,
                    color: kc.quoteWatermark,
                  ),
                ),
              ),
              ConstrainedBox(
                constraints: const BoxConstraints(
                  minHeight: 150,
                  minWidth: double.infinity,
                ),
                child: Padding(
                  padding: EdgeInsets.fromLTRB(20, 20, 20, wide ? 18 : 16),
                  child: body,
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}

enum _HeaderPillTone {
  /// Primary action on the banner: the theme's capsule — white + capsule
  /// shadow with #15131F text in light (18.3:1), tonal #24202F @ 92 % +
  /// hairline with #F4F3F8 text in dark (≥ 11:1).
  capsule,

  /// Secondary / "on" state: `OnColorChip` glass (black 22 %, high
  /// contrast: solid ink) with white text (≥ 7:1 on every palette).
  glass,
}

/// 40 px pill on the gradient banner (44 px hit box).
class _HeaderPill extends StatelessWidget {
  const _HeaderPill({
    required this.icon,
    required this.label,
    required this.onTap,
    required this.tone,
    this.iconColor,
    this.selected,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final _HeaderPillTone tone;

  /// Overrides the tone's icon colour (the followed star is gold).
  final Color? iconColor;
  final bool? selected;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final highContrast = MediaQuery.maybeHighContrastOf(context) ?? false;
    final (bg, fg) = switch (tone) {
      _HeaderPillTone.capsule => (kc.capsule, kc.onCapsuleText),
      _HeaderPillTone.glass => (
          highContrast ? KararGlass.chipHighContrast : KararGlass.chipOnColor,
          kc.onColor,
        ),
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
          border: tone == _HeaderPillTone.capsule ? kc.capsuleBorder : null,
          boxShadow: tone == _HeaderPillTone.capsule
              ? KararShadow.capsuleOf(Theme.of(context).brightness)
              : null,
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 18, color: iconColor ?? fg),
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

    // Not followed: the capsule CTA. Followed: glass with a gold star.
    return _HeaderPill(
      tone: isFollowed ? _HeaderPillTone.glass : _HeaderPillTone.capsule,
      iconColor: isFollowed ? context.kc.gold : null,
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
      tone: _HeaderPillTone.glass,
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
