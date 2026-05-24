import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/skeleton.dart';
import 'categories_provider.dart';
import 'discover_provider.dart';
import 'post_card.dart';

class DiscoverScreen extends ConsumerWidget {
  const DiscoverScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final categoriesAsync = ref.watch(categoriesProvider);
    final discoverAsync = ref.watch(discoverProvider);

    return Scaffold(
      appBar: AppBar(
        leadingWidth: 96,
        leading: InkWell(
          onTap: () => context.go('/'),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
            child: Image.asset('logo/logo.png', fit: BoxFit.contain),
          ),
        ),
        title: const Text('Keşfet'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: RefreshIndicator(
          onRefresh: () => ref.refresh(discoverProvider.future),
          child: CustomScrollView(
            slivers: [
              // --- Kategoriler ---
              const SliverToBoxAdapter(
                child: Padding(
                  padding: EdgeInsets.fromLTRB(16, 16, 16, 8),
                  child: Text(
                    'Kategoriler',
                    style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                  ),
                ),
              ),
              categoriesAsync.when(
                data: (categories) => SliverPadding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  sliver: SliverGrid(
                    gridDelegate:
                        SliverGridDelegateWithFixedCrossAxisCount(
                      crossAxisCount: context.isDesktop ? 4 : 2,
                      mainAxisSpacing: 10,
                      crossAxisSpacing: 10,
                      childAspectRatio: 2.5,
                    ),
                    delegate: SliverChildBuilderDelegate(
                      (context, index) {
                        final category = categories[index];
                        if (category.id == 0) return const SizedBox.shrink();
                        return _CategoryCard(category: category);
                      },
                      childCount: categories.length,
                    ),
                  ),
                ),
                loading: () => SliverPadding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  sliver: SliverGrid(
                    gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
                      crossAxisCount: context.isDesktop ? 4 : 2,
                      mainAxisSpacing: 10,
                      crossAxisSpacing: 10,
                      childAspectRatio: 2.5,
                    ),
                    delegate: const SliverChildBuilderDelegate(
                      _buildCategorySkeleton,
                      childCount: 8,
                    ),
                  ),
                ),
                error: (_, __) => const SliverToBoxAdapter(
                  child: Center(child: Text('Kategoriler yüklenemedi')),
                ),
              ),
              // --- Keşfet bölümleri ---
              discoverAsync.when(
                data: (data) => _DiscoverSections(data: data),
                loading: () => SliverList(
                  delegate: SliverChildBuilderDelegate(
                    (_, i) => i == 0
                        ? const _DiscoverSectionSkeleton()
                        : const _DiscoverSectionSkeleton(),
                    childCount: 2,
                  ),
                ),
                error: (error, _) => SliverToBoxAdapter(
                  child: Padding(
                    padding: const EdgeInsets.all(24),
                    child: ErrorView(
                      message: 'Keşfet yüklenemedi',
                      onRetry: () => ref.refresh(discoverProvider),
                    ),
                  ),
                ),
              ),
              const SliverToBoxAdapter(child: SizedBox(height: 24)),
            ],
          ),
        ),
      ),
    );
  }
}

class _DiscoverSections extends StatelessWidget {
  const _DiscoverSections({required this.data});

  final DiscoverData data;

  @override
  Widget build(BuildContext context) {
    return SliverList(
      delegate: SliverChildListDelegate([
        if (data.rising.isNotEmpty) ...[
          const _SectionHeader(
            icon: Icons.trending_up_rounded,
            iconColor: AppColors.hakli,
            title: 'Yükselenler',
            subtitle: 'Son 6 saatte hızla oy alan tartışmalar',
          ),
          _HorizontalPostList(posts: data.rising),
        ],
        if (data.controversial.isNotEmpty) ...[
          const _SectionHeader(
            icon: Icons.balance_rounded,
            iconColor: AppColors.primary,
            title: 'Topluluk İkiye Bölündü',
            subtitle: 'Kamuoyunun tam ortasında kalan kararlar',
          ),
          _HorizontalPostList(posts: data.controversial),
        ],
        if (data.fresh.isNotEmpty) ...[
          const _SectionHeader(
            icon: Icons.fiber_new_rounded,
            iconColor: AppColors.haksiz,
            title: 'Yeni Karar Bekleyenler',
            subtitle: 'Henüz az oy almış, görüşünü bekleyen konular',
          ),
          _HorizontalPostList(posts: data.fresh),
        ],
        // Serendipity section based on docs/recommendation-system.md
        if (data.rising.isNotEmpty && data.rising.length > 5) ...[
          const _SectionHeader(
            icon: Icons.auto_awesome,
            iconColor: AppColors.primary,
            title: 'Farklı Bir Şey',
            subtitle: 'İlgilenebileceğin yeni kategoriler keşfet',
          ),
          _HorizontalPostList(posts: data.rising.reversed.toList()),
        ],
        if (data.cityTrending.isNotEmpty && data.city != null) ...[
          _SectionHeader(
            icon: Icons.location_city_rounded,
            iconColor: AppColors.textSecondary,
            title: '${data.city}\'da Trend',
            subtitle: 'Bulunduğun şehirde öne çıkan tartışmalar',
          ),
          _HorizontalPostList(posts: data.cityTrending),
        ],
        if (data.trendTopics.isNotEmpty) ...[
          const _SectionHeader(
            icon: Icons.local_fire_department_rounded,
            iconColor: AppColors.haksiz,
            title: 'Trend Konular',
          ),
          _TrendTopicsSection(topics: data.trendTopics),
        ],
        if (data.todaysPosts.isNotEmpty) ...[
          const _SectionHeader(
            icon: Icons.wb_sunny_rounded,
            iconColor: AppColors.accent,
            title: 'Günün Kararları',
            subtitle: 'Bugün en çok oy alan tartışmalar',
          ),
          _TodaysPostsList(posts: data.todaysPosts),
        ],
        if (data.rising.isEmpty &&
            data.controversial.isEmpty &&
            data.fresh.isEmpty)
          const Padding(
            padding: EdgeInsets.all(48),
            child: Center(
              child: Text(
                'Şu an keşfedilecek içerik yok.\nBiraz sonra tekrar dene.',
                textAlign: TextAlign.center,
                style: TextStyle(color: AppColors.textSecondary),
              ),
            ),
          ),
      ]),
    );
  }
}

class _SectionHeader extends StatelessWidget {
  const _SectionHeader({
    required this.icon,
    required this.iconColor,
    required this.title,
    this.subtitle,
  });

  final IconData icon;
  final Color iconColor;
  final String title;
  final String? subtitle;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 24, 16, 10),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: iconColor, size: 20),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: const TextStyle(
                      fontSize: 17, fontWeight: FontWeight.bold),
                ),
                if (subtitle != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    subtitle!,
                    style: const TextStyle(
                      fontSize: 12,
                      color: AppColors.textSecondary,
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

class _HorizontalPostList extends StatelessWidget {
  const _HorizontalPostList({required this.posts});

  final List<Post> posts;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 340,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 16),
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
    );
  }
}

Widget _buildCategorySkeleton(BuildContext context, int index) {
  return const Skeleton(
      height: double.infinity, width: double.infinity, borderRadius: 12);
}

class _DiscoverSectionSkeleton extends StatelessWidget {
  const _DiscoverSectionSkeleton();

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Padding(
          padding: EdgeInsets.fromLTRB(16, 24, 16, 10),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Skeleton(height: 18, width: 160),
              SizedBox(height: 6),
              Skeleton(height: 12, width: 240),
            ],
          ),
        ),
        SizedBox(
          height: 340,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: 16),
            itemCount: 3,
            separatorBuilder: (_, __) => const SizedBox(width: 10),
            itemBuilder: (_, __) => const SizedBox(
              width: 280,
              child: PostCardSkeleton(),
            ),
          ),
        ),
      ],
    );
  }
}

class _TrendTopicsSection extends StatelessWidget {
  const _TrendTopicsSection({required this.topics});

  final List<TrendTopic> topics;

  String _formatCount(int n) {
    if (n >= 1000) return '${(n / 1000).toStringAsFixed(0)}B';
    return n.toString();
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16),
      child: Column(
        children: [
          for (int i = 0; i < topics.length; i++)
            InkWell(
              onTap: () => context.push('/search?q=${topics[i].name}'),
              borderRadius: BorderRadius.circular(8),
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 10),
                child: Row(
                  children: [
                    Text(
                      '${i + 1}.',
                      style: Theme.of(context).textTheme.bodySmall?.copyWith(
                            color: AppColors.textTertiary,
                            fontWeight: FontWeight.w700,
                          ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            '#${topics[i].name}',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: Theme.of(context)
                                .textTheme
                                .bodyMedium
                                ?.copyWith(fontWeight: FontWeight.w700),
                          ),
                          Text(
                            '${_formatCount(topics[i].postCount)} gönderi',
                            style: Theme.of(context)
                                .textTheme
                                .bodySmall
                                ?.copyWith(color: AppColors.textSecondary),
                          ),
                        ],
                      ),
                    ),
                    if (topics[i].growthPercent != null)
                      Container(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 8, vertical: 3),
                        decoration: BoxDecoration(
                          color: AppColors.hakli.withValues(alpha: 0.12),
                          borderRadius: BorderRadius.circular(12),
                        ),
                        child: Text(
                          '+%${topics[i].growthPercent}',
                          style: const TextStyle(
                            color: AppColors.hakli,
                            fontSize: 11,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
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

class _TodaysPostsList extends StatelessWidget {
  const _TodaysPostsList({required this.posts});

  final List<Post> posts;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16),
      child: Column(
        children: posts
            .map(
              (post) => Padding(
                padding: const EdgeInsets.only(bottom: 10),
                child: PostCard(
                  post: post,
                  onTap: () => context.push('/posts/${post.id}', extra: post),
                ),
              ),
            )
            .toList(),
      ),
    );
  }
}

class _CategoryCard extends StatelessWidget {
  const _CategoryCard({required this.category});

  final Category category;

  @override
  Widget build(BuildContext context) {
    return Card(
      elevation: 0,
      color: AppColors.surfaceVariant.withValues(alpha: 0.5),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: BorderSide(
          color: Theme.of(context).dividerColor.withValues(alpha: 0.05),
        ),
      ),
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: () => context.push('/categories/${category.id}'),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12),
          child: Row(
            children: [
              Text(category.icon, style: const TextStyle(fontSize: 20)),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  category.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
