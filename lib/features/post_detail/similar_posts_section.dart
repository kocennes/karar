import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/count_format.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karar_surface.dart';
import '../../shared/widgets/post_surface.dart';
import '../../shared/widgets/skeleton.dart';
import '../../shared/widgets/verdict_chip.dart';
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
    final kc = context.kc;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: Semantics(
            header: true,
            child: Text(
              'Benzer Kararlar',
              style: AppTypography.headline.copyWith(color: kc.text1),
            ),
          ),
        ),
        for (var i = 0; i < posts.length; i++) ...[
          if (i > 0) const SizedBox(height: 12),
          _SimilarPostTile(
            post: posts[i],
            onTap: () => context.push('/posts/${posts[i].id}', extra: posts[i]),
          ),
        ],
      ],
    );
  }
}

/// Compact post row (spec §5.4 "compact variant"): category tile, title +
/// meta, optional photo thumb, the on-surface verdict chip and the comment
/// count. Local to the detail screen until the feed's
/// `PostCard(variant: compact)` lands.
class _SimilarPostTile extends StatelessWidget {
  const _SimilarPostTile({required this.post, required this.onTap});

  final Post post;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final style = CategoryStyle.of(post.category);
    final imageUrl = PostSurface.imageUrlOf(post);

    return Semantics(
      container: true,
      button: true,
      label: 'Gönderi: ${post.title}',
      onTap: onTap,
      onTapHint: 'Detayları gör',
      excludeSemantics: true,
      child: KararSurface(
        padding: const EdgeInsets.all(16),
        onTap: onTap,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                ExcludeSemantics(
                  child: Container(
                    width: 44,
                    height: 44,
                    decoration: BoxDecoration(
                      gradient: style.gradient,
                      borderRadius: KararRadius.all(KararRadius.iconTile),
                    ),
                    child: Icon(style.icon, size: 22, color: Colors.white),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        post.title,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: AppTypography.bodyStrong.copyWith(
                          fontSize: 16,
                          height: 1.3,
                          fontWeight: FontWeight.w800,
                          color: kc.text1,
                        ),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        '${post.category.name} · ${post.createdAgo}',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: AppTypography.meta.copyWith(color: kc.text3),
                      ),
                    ],
                  ),
                ),
                if (imageUrl != null) ...[
                  const SizedBox(width: 12),
                  ExcludeSemantics(
                    child: ClipRRect(
                      borderRadius: KararRadius.all(KararRadius.input),
                      child: SizedBox(
                        width: 64,
                        height: 64,
                        child: ColoredBox(
                          color: kc.muted,
                          child: CachedNetworkImage(
                            imageUrl: imageUrl,
                            fit: BoxFit.cover,
                            memCacheWidth: 192,
                            errorWidget: (_, __, ___) =>
                                const SizedBox.shrink(),
                          ),
                        ),
                      ),
                    ),
                  ),
                ],
              ],
            ),
            const SizedBox(height: 12),
            VerdictChip(post: post, tone: VerdictChipTone.onSurface),
            const SizedBox(height: 8),
            ExcludeSemantics(
              child: Row(
                children: [
                  Icon(
                    Icons.chat_bubble_outline_rounded,
                    size: 16,
                    color: kc.text2,
                  ),
                  const SizedBox(width: 6),
                  Text(
                    formatCompactCount(post.commentCount),
                    style: AppTypography.meta.copyWith(
                      color: kc.text2,
                      fontFeatures: AppTypography.tabular,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _SimilarPostsSkeleton extends StatelessWidget {
  const _SimilarPostsSkeleton();

  @override
  Widget build(BuildContext context) {
    return const Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: EdgeInsets.only(bottom: 12),
          child: Skeleton(height: 22, width: 170),
        ),
        Skeleton(
          height: 132,
          width: double.infinity,
          borderRadius: KararRadius.surface,
        ),
        SizedBox(height: 12),
        Skeleton(
          height: 132,
          width: double.infinity,
          borderRadius: KararRadius.surface,
        ),
      ],
    );
  }
}
