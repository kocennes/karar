import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/theme/app_colors.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/login_nudge.dart';
import '../../shared/widgets/skeleton.dart';
import '../../shared/widgets/centered_content.dart';
import 'categories_provider.dart';
import 'category_feed_provider.dart';
import 'post_card.dart';

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
        title: Text(
          category != null ? '${category.icon} ${category.name}' : 'Kategori',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        centerTitle: true,
        actions: [
          _FollowButton(categoryId: categoryId),
          const SizedBox(width: 4),
        ],
      ),
      body: CenteredContent(
        child: Column(
          children: [
            _SortBar(categoryId: categoryId),
            Expanded(child: _CategoryFeedList(categoryId: categoryId)),
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

    return TextButton.icon(
      onPressed: () {
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
      icon: Icon(
        isFollowed ? Icons.star_rounded : Icons.star_border_rounded,
        size: 20,
        color: isFollowed ? AppColors.primary : null,
      ),
      label: Text(
        isFollowed ? 'Takip ediliyor' : 'Takip Et',
        style: TextStyle(
          color: isFollowed ? AppColors.primary : null,
          fontWeight: isFollowed ? FontWeight.w600 : null,
        ),
      ),
    );
  }
}

class _SortBar extends ConsumerWidget {
  const _SortBar({required this.categoryId});

  final int categoryId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final sort =
        ref.watch(categoryFeedProvider(categoryId).select((s) => s.sort));

    return Container(
      decoration: BoxDecoration(
        border: Border(
          bottom: BorderSide(
            color: Theme.of(context).dividerColor,
            width: 0.5,
          ),
        ),
      ),
      child: Row(
        children: [
          _SortChip(
            icon: Icons.local_fire_department_rounded,
            label: 'Trend',
            value: 'trending',
            current: sort,
            onTap: () => ref
                .read(categoryFeedProvider(categoryId).notifier)
                .changeSort('trending'),
          ),
          _SortChip(
            icon: Icons.access_time_rounded,
            label: 'Yeni',
            value: 'new',
            current: sort,
            onTap: () => ref
                .read(categoryFeedProvider(categoryId).notifier)
                .changeSort('new'),
          ),
        ],
      ),
    );
  }
}

class _SortChip extends StatelessWidget {
  const _SortChip({
    required this.icon,
    required this.label,
    required this.value,
    required this.current,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final String value;
  final String current;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final selected = value == current;
    return InkWell(
      onTap: onTap,
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 150),
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        decoration: BoxDecoration(
          border: Border(
            bottom: BorderSide(
              color: selected ? AppColors.primary : Colors.transparent,
              width: 2,
            ),
          ),
        ),
        child: Row(
          children: [
            Icon(
              icon,
              size: 16,
              color: selected ? AppColors.primary : AppColors.textSecondary,
            ),
            const SizedBox(width: 6),
            Text(
              label,
              style: TextStyle(
                fontWeight: selected ? FontWeight.w600 : FontWeight.normal,
                color: selected ? AppColors.primary : AppColors.textSecondary,
                fontSize: 14,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _CategoryFeedList extends ConsumerStatefulWidget {
  const _CategoryFeedList({required this.categoryId});

  final int categoryId;

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

    if (feedState.isLoading) {
      return ListView.separated(
        padding: const EdgeInsets.symmetric(vertical: 8),
        itemCount: 5,
        separatorBuilder: (_, __) => const SizedBox(height: 8),
        itemBuilder: (_, __) => const Padding(
          padding: EdgeInsets.symmetric(horizontal: 12),
          child: PostCardSkeleton(),
        ),
      );
    }

    if (feedState.error != null && feedState.posts.isEmpty) {
      return ErrorView(
        message: 'Gönderiler yüklenemedi',
        onRetry: () => ref
            .read(categoryFeedProvider(widget.categoryId).notifier)
            .refresh(),
      );
    }

    if (feedState.posts.isEmpty) {
      return const EmptyState(
        icon: Icons.inbox_outlined,
        message: 'Bu kategoride henüz paylaşım yapılmamış.',
      );
    }

    return RefreshIndicator(
      onRefresh: () =>
          ref.read(categoryFeedProvider(widget.categoryId).notifier).refresh(),
      child: ListView.separated(
        controller: _scrollController,
        padding: const EdgeInsets.only(top: 8, bottom: 24),
        itemCount: feedState.posts.length + (feedState.isLoadingMore ? 1 : 0),
        separatorBuilder: (_, __) => const SizedBox(height: 8),
        itemBuilder: (context, index) {
          if (index == feedState.posts.length) {
            return const Padding(
              padding: EdgeInsets.symmetric(vertical: 16),
              child: Center(
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            );
          }
          final post = feedState.posts[index];
          return Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12),
            child: PostCard(
              post: post,
              onTap: () => context.push('/posts/${post.id}', extra: post),
            ),
          );
        },
      ),
    );
  }
}
