import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/loading_indicator.dart';
import '../../shared/widgets/centered_content.dart';
import '../feed/feed_provider.dart';
import 'my_posts_provider.dart';
import 'widgets/column_app_bar.dart';
import 'widgets/verdict_tile.dart';

const _sortOptions = [
  ('new', 'En Yeni'),
  ('old', 'En Eski'),
  ('votes', 'En Çok Oy'),
  ('comments', 'En Çok Yorum'),
];

class MyPostsScreen extends ConsumerWidget {
  const MyPostsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Scaffold(
      appBar: ColumnAppBar(
        title: const Text('Paylaşımlarım'),
        centerTitle: true,
        actions: [
          Consumer(
            builder: (context, ref, _) {
              final state = ref.watch(myPostsProvider);
              return PopupMenuButton<String>(
                icon: const Icon(Icons.sort_rounded),
                tooltip: 'Sırala',
                onSelected: (s) =>
                    ref.read(myPostsProvider.notifier).setSort(s),
                itemBuilder: (_) => _sortOptions
                    .map((o) => PopupMenuItem(
                          value: o.$1,
                          child: Row(
                            children: [
                              if (state.sort == o.$1)
                                const Icon(Icons.check_rounded, size: 18)
                              else
                                const SizedBox(width: 18),
                              const SizedBox(width: 10),
                              Text(o.$2),
                            ],
                          ),
                        ))
                    .toList(),
              );
            },
          ),
        ],
      ),
      body: const CenteredContent(
        child: MyPostsGrid(showOwnerMenu: true),
      ),
    );
  }
}

/// The signed-in user's posts from [myPostsProvider] as a grid of
/// [VerdictTile]s. Posts that are not active show their moderation status
/// in place of the verdict pill.
///
/// With [sliver] the widget builds a sliver for an enclosing
/// `CustomScrollView` (profile "Gönderiler" tab); otherwise it is a
/// self-contained, pull-to-refresh grid (the `/profile/posts` route).
/// [showOwnerMenu] adds the owner "…" menu (Sil) to every tile.
class MyPostsGrid extends ConsumerWidget {
  const MyPostsGrid({
    super.key,
    this.sliver = false,
    this.showOwnerMenu = false,
  });

  final bool sliver;
  final bool showOwnerMenu;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(myPostsProvider);
    final content = _buildSliver(context, ref, state);
    if (sliver) return content;

    final scrollView = CustomScrollView(
      physics: const AlwaysScrollableScrollPhysics(),
      slivers: [
        content,
        SliverToBoxAdapter(
          child: SizedBox(height: context.navBottomInset + 24),
        ),
      ],
    );
    if (state.posts.isEmpty) return scrollView;
    return RefreshIndicator(
      onRefresh: () => ref.read(myPostsProvider.notifier).load(),
      child: scrollView,
    );
  }

  Widget _buildSliver(BuildContext context, WidgetRef ref, MyPostsState state) {
    if (state.isLoading && state.posts.isEmpty) {
      return const SliverFillRemaining(
        hasScrollBody: false,
        child: Padding(
          padding: EdgeInsets.all(32),
          child: LoadingIndicator(),
        ),
      );
    }

    if (state.error != null && state.posts.isEmpty) {
      return SliverFillRemaining(
        hasScrollBody: false,
        child: ErrorView(
          message: state.error!,
          onRetry: () => ref.read(myPostsProvider.notifier).load(),
        ),
      );
    }

    if (state.posts.isEmpty) {
      return const SliverFillRemaining(
        hasScrollBody: false,
        child: EmptyState(
          message: 'Henüz bir durum paylaşmadın. Paylaşınca burada göreceksin.',
          icon: Icons.post_add_rounded,
        ),
      );
    }

    return SliverPadding(
      padding: const EdgeInsets.all(16),
      sliver: SliverGrid(
        gridDelegate: VerdictTile.gridDelegate(context),
        delegate: SliverChildBuilderDelegate(
          (context, index) {
            final post = state.posts[index];
            return VerdictTile(
              post: post,
              onTap: () => context.push('/posts/${post.id}', extra: post),
              footer: post.status != 'active'
                  ? _ModerationStatusPanel(post: post)
                  : null,
              trailingAction: showOwnerMenu && post.isOwner
                  ? _OwnerMenu(
                      post: post,
                      onDeleted: () => ref
                          .read(myPostsProvider.notifier)
                          .removePost(post.id),
                    )
                  : null,
            );
          },
          childCount: state.posts.length,
        ),
      ),
    );
  }
}

class _ModerationStatusPanel extends StatelessWidget {
  const _ModerationStatusPanel({required this.post});
  final Post post;

  @override
  Widget build(BuildContext context) {
    final (icon, label) = switch (post.status) {
      'under_review' => (Icons.hourglass_top_rounded, 'İncelemede'),
      'auto_hidden' => (Icons.visibility_off_rounded, 'Gizlendi'),
      _ => (Icons.block_rounded, 'Kaldırıldı'),
    };
    return TileStatusPanel(
      icon: icon,
      label: label,
      reason: post.moderationReason,
    );
  }
}

/// Owner "…" menu on a tile: the same delete flow as the feed card's menu
/// (confirm dialog → `deletePost` → drop from the feed and from
/// [myPostsProvider] via [onDeleted]).
class _OwnerMenu extends ConsumerWidget {
  const _OwnerMenu({required this.post, required this.onDeleted});

  final Post post;
  final VoidCallback onDeleted;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final kc = context.kc;
    return PopupMenuButton<String>(
      tooltip: 'Gönderi seçenekleri',
      icon: Container(
        width: VerdictTile.menuDiameter,
        height: VerdictTile.menuDiameter,
        decoration: const BoxDecoration(
          color: KararGlass.chipOnColor,
          shape: BoxShape.circle,
        ),
        child: Icon(
          Icons.more_horiz_rounded,
          size: 18,
          color: kc.onColor,
        ),
      ),
      padding: EdgeInsets.zero,
      constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
      onSelected: (value) {
        if (value == 'delete') _confirmDelete(context, ref);
      },
      itemBuilder: (context) => [
        PopupMenuItem(
          value: 'delete',
          child: Row(
            children: [
              Icon(Icons.delete_outline_rounded,
                  size: 20, color: kc.haksizText),
              const SizedBox(width: 10),
              Text('Sil', style: TextStyle(color: kc.haksizText)),
            ],
          ),
        ),
      ],
    );
  }

  void _confirmDelete(BuildContext context, WidgetRef ref) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Gönderiyi Sil?'),
        content: const Text(
          'Bu gönderi kalıcı olarak silinecektir. Bu işlem geri alınamaz.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            style: FilledButton.styleFrom(
              backgroundColor: ctx.kc.haksizFill,
              foregroundColor: ctx.kc.onColor,
            ),
            onPressed: () async {
              final messenger = ScaffoldMessenger.of(context);
              Navigator.pop(ctx);
              try {
                await ref.read(postRepositoryProvider).deletePost(post.id);
                ref.read(feedProvider.notifier).removePost(post.id);
                onDeleted();
                messenger.showSnackBar(
                  const SnackBar(content: Text('Gönderi silindi.')),
                );
              } catch (_) {
                messenger.showSnackBar(
                  const SnackBar(content: Text('Bir hata oluştu.')),
                );
              }
            },
            child: const Text('Sil'),
          ),
        ],
      ),
    );
  }
}
