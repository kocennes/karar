import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/skeleton.dart';
import '../feed/post_card.dart';
import 'similar_posts_provider.dart';

class SimilarPostsSection extends ConsumerWidget {
  const SimilarPostsSection({super.key, required this.postId});

  final String postId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final similarAsync = ref.watch(similarPostsProvider(postId));

    return similarAsync.when(
      loading: () => const _SimilarPostsSkeleton(),
      error: (_, __) => const SizedBox.shrink(),
      data: (posts) {
        if (posts.isEmpty) return const SizedBox.shrink();
        return _SimilarPostsContent(posts: posts);
      },
    );
  }
}

class _SimilarPostsContent extends StatelessWidget {
  const _SimilarPostsContent({required this.posts});

  final List<Post> posts;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(0, 8, 0, 12),
          child: Row(
            children: [
              Container(
                width: 3,
                height: 18,
                decoration: BoxDecoration(
                  color: AppColors.primary,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
              const SizedBox(width: 10),
              Text(
                'Benzer Kararlar',
                style: Theme.of(context).textTheme.titleMedium?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
              ),
            ],
          ),
        ),
        SizedBox(
          height: 270,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.only(bottom: 4),
            itemCount: posts.length,
            separatorBuilder: (_, __) => const SizedBox(width: 10),
            itemBuilder: (context, index) {
              final post = posts[index];
              return SizedBox(
                width: 280,
                child: PostCard(
                  post: post,
                  onTap: () => context.push('/posts/${post.id}', extra: post),
                ),
              );
            },
          ),
        ),
      ],
    );
  }
}

class _SimilarPostsSkeleton extends StatelessWidget {
  const _SimilarPostsSkeleton();

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Padding(
          padding: EdgeInsets.fromLTRB(0, 8, 0, 12),
          child: Skeleton(height: 18, width: 160),
        ),
        SizedBox(
          height: 270,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.only(bottom: 4),
            itemCount: 3,
            separatorBuilder: (_, __) => const SizedBox(width: 10),
            itemBuilder: (_, __) =>
                const SizedBox(width: 280, child: PostCardSkeleton()),
          ),
        ),
      ],
    );
  }
}
