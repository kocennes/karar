import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/skeleton.dart';
import '../../shared/widgets/centered_content.dart';
import '../feed/post_card.dart';
import 'my_posts_provider.dart';

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
      appBar: AppBar(
        title: const Text('Paylaşımlarım'),
        centerTitle: true,
        actions: [
          Consumer(
            builder: (context, ref, _) {
              final state = ref.watch(myPostsProvider);
              return PopupMenuButton<String>(
                icon: const Icon(Icons.sort),
                tooltip: 'Sırala',
                onSelected: (s) => ref.read(myPostsProvider.notifier).setSort(s),
                itemBuilder: (_) => _sortOptions
                    .map((o) => PopupMenuItem(
                          value: o.$1,
                          child: Row(
                            children: [
                              if (state.sort == o.$1)
                                const Icon(Icons.check, size: 16)
                              else
                                const SizedBox(width: 16),
                              const SizedBox(width: 8),
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
        child: _MyPostsList(),
      ),
    );
  }
}

class _MyPostsList extends ConsumerWidget {
  const _MyPostsList();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(myPostsProvider);

    if (state.isLoading && state.posts.isEmpty) {
      return ListView.separated(
        padding: const EdgeInsets.all(16),
        itemCount: 3,
        separatorBuilder: (_, __) => const SizedBox(height: 10),
        itemBuilder: (_, __) => const PostCardSkeleton(),
      );
    }

    if (state.error != null && state.posts.isEmpty) {
      return ErrorView(
        message: state.error!,
        onRetry: () => ref.read(myPostsProvider.notifier).load(),
      );
    }

    if (state.posts.isEmpty) {
      return const EmptyState(
        message: 'Henüz bir durum paylaşmadın. Paylaşınca burada göreceksin.',
        icon: Icons.post_add_outlined,
      );
    }

    return RefreshIndicator(
      onRefresh: () => ref.read(myPostsProvider.notifier).load(),
      child: ListView.separated(
        padding: const EdgeInsets.all(16),
        itemCount: state.posts.length,
        separatorBuilder: (_, __) => const SizedBox(height: 10),
        itemBuilder: (context, index) {
          final post = state.posts[index];
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (post.status != 'active') _ModerationStatusBanner(post: post),
              PostCard(
                post: post,
                onTap: () => context.push('/posts/${post.id}', extra: post),
              ),
            ],
          );
        },
      ),
    );
  }
}

class _ModerationStatusBanner extends StatelessWidget {
  const _ModerationStatusBanner({required this.post});
  final Post post;

  @override
  Widget build(BuildContext context) {
    final (icon, label, color) = switch (post.status) {
      'under_review' => (
          Icons.hourglass_top_rounded,
          'İncelemede',
          Colors.orange,
        ),
      'auto_hidden' => (
          Icons.visibility_off_outlined,
          'Gizlendi',
          AppColors.haksiz,
        ),
      _ => (
          Icons.block_outlined,
          'Kaldırıldı',
          AppColors.haksiz,
        ),
    };

    return Container(
      margin: const EdgeInsets.only(bottom: 4),
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.08),
        borderRadius: const BorderRadius.vertical(top: Radius.circular(12)),
        border: Border.all(color: color.withValues(alpha: 0.3)),
      ),
      child: Row(
        children: [
          Icon(icon, size: 16, color: color),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  label,
                  style: TextStyle(
                    color: color,
                    fontWeight: FontWeight.w700,
                    fontSize: 12,
                  ),
                ),
                if (post.moderationReason != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    post.moderationReason!,
                    style: TextStyle(
                      color: color.withValues(alpha: 0.8),
                      fontSize: 11,
                    ),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

