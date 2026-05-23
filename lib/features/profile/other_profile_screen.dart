import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../../core/api/api_exception.dart';
import '../../core/auth/auth_service.dart';
import '../../core/providers.dart';
import '../../core/utils/date_formatter.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/content_unavailable_view.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karma_badge.dart';
import '../../shared/widgets/loading_indicator.dart';
import '../feed/feed_provider.dart';
import '../feed/post_card.dart';
import '../report/report_bottom_sheet.dart';

class OtherProfileScreen extends ConsumerStatefulWidget {
  const OtherProfileScreen({super.key, required this.username});
  final String username;

  @override
  ConsumerState<OtherProfileScreen> createState() => _OtherProfileScreenState();
}

class _OtherProfileScreenState extends ConsumerState<OtherProfileScreen>
    with SingleTickerProviderStateMixin {
  AuthUser? _user;
  List<Post> _posts = [];
  List<MyComment> _comments = [];
  bool _isLoading = true;
  String? _error;
  String? _errorCode;

  late final TabController _tabController;

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: 2, vsync: this);
    _load();
  }

  @override
  void dispose() {
    _tabController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _isLoading = true;
      _error = null;
      _errorCode = null;
    });
    try {
      final results = await Future.wait([
        ref.read(authServiceProvider).fetchUserProfile(widget.username),
        ref.read(postRepositoryProvider).fetchUserPosts(widget.username),
        ref.read(postRepositoryProvider).fetchUserComments(widget.username),
      ]);
      setState(() {
        _user = results[0] as AuthUser;
        _posts = results[1] as List<Post>;
        _comments = results[2] as List<MyComment>;
        _isLoading = false;
      });
    } on ApiException catch (e) {
      setState(() {
        _error = e.friendlyMessage;
        _errorCode = e.code;
        _isLoading = false;
      });
    } catch (_) {
      setState(() {
        _error = 'Profil yüklenemedi.';
        _isLoading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_isLoading) return const Scaffold(body: LoadingIndicator());
    if (_error != null) {
      if (_errorCode == 'USER_NOT_FOUND' || _errorCode == 'HTTP_404') {
        return Scaffold(
          appBar: AppBar(title: const Text('Kullanıcı bulunamadı')),
          body: ContentUnavailableView(
            icon: Icons.person_off_outlined,
            title: 'Kullanıcı bulunamadı',
            message: 'Hesap silinmiş veya görünür değil.',
            buttonLabel: 'Geri dön',
            onPressed: () {
              if (context.canPop()) {
                context.pop();
              } else {
                context.go('/');
              }
            },
          ),
        );
      }
      return Scaffold(body: ErrorView(message: _error!, onRetry: _load));
    }
    if (_user == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('Kullanıcı bulunamadı')),
        body: ContentUnavailableView(
          icon: Icons.person_off_outlined,
          title: 'Kullanıcı bulunamadı',
          message: 'Hesap silinmiş veya görünür değil.',
          buttonLabel: 'Geri dön',
          onPressed: () {
            if (context.canPop()) {
              context.pop();
            } else {
              context.go('/');
            }
          },
        ),
      );
    }

    final user = _user!;

    return Scaffold(
      appBar: AppBar(
        title: Text(
          '@${user.username}',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.block, color: Colors.red),
            tooltip: 'Engelle',
            onPressed: () => _confirmBlock(context, ref, user),
          ),
          IconButton(
            icon: const Icon(Icons.flag_outlined),
            tooltip: 'Şikayet Et',
            onPressed: () {
              ReportBottomSheet.show(
                context,
                targetType: 'user',
                targetId: user.id,
                repository: ref.read(postRepositoryProvider),
              );
            },
          ),
        ],
      ),
      body: NestedScrollView(
        headerSliverBuilder: (context, innerBoxIsScrolled) => [
          SliverToBoxAdapter(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: _UserHeader(user: user),
            ),
          ),
          SliverPersistentHeader(
            pinned: true,
            delegate: _SliverAppBarDelegate(
              TabBar(
                controller: _tabController,
                indicatorSize: TabBarIndicatorSize.label,
                tabs: [
                  Tab(text: 'Postlar (${user.postCount})'),
                  Tab(text: 'Yorumlar (${user.commentCount})'),
                ],
              ),
            ),
          ),
        ],
        body: TabBarView(
          controller: _tabController,
          children: [
            _PostsTab(posts: _posts),
            _CommentsTab(comments: _comments),
          ],
        ),
      ),
    );
  }

  void _confirmBlock(BuildContext context, WidgetRef ref, AuthUser user) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Engelle?'),
        content: Text(
            '${user.username} kullanıcısını engellemek istediğine emin misin?'),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(ctx), child: const Text('Vazgeç')),
          FilledButton(
            onPressed: () async {
              Navigator.pop(ctx);
              try {
                await ref.read(authServiceProvider).blockUser(user.id);
                ref.read(feedProvider.notifier).removePostsByAuthor(user.id);
                if (context.mounted) {
                  Navigator.pop(context);
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(
                      content: Text('@${user.username} engellendi.'),
                      action: SnackBarAction(
                        label: 'Geri Al',
                        onPressed: () async {
                          try {
                            await ref
                                .read(authServiceProvider)
                                .unblockUser(user.id);
                            ref.read(feedProvider.notifier).refresh();
                          } catch (_) {}
                        },
                      ),
                    ),
                  );
                }
              } catch (_) {}
            },
            child: const Text('Engelle'),
          ),
        ],
      ),
    );
  }
}

class _SliverAppBarDelegate extends SliverPersistentHeaderDelegate {
  _SliverAppBarDelegate(this._tabBar);

  final TabBar _tabBar;

  @override
  double get minExtent => _tabBar.preferredSize.height;
  @override
  double get maxExtent => _tabBar.preferredSize.height;

  @override
  Widget build(
      BuildContext context, double shrinkOffset, bool overlapsContent) {
    return Container(
      color: Theme.of(context).scaffoldBackgroundColor,
      child: _tabBar,
    );
  }

  @override
  bool shouldRebuild(_SliverAppBarDelegate oldDelegate) {
    return false;
  }
}

class _PostsTab extends StatelessWidget {
  const _PostsTab({required this.posts});
  final List<Post> posts;

  @override
  Widget build(BuildContext context) {
    if (posts.isEmpty) {
      return const Center(
        child: Padding(
          padding: EdgeInsets.all(32),
          child: Text('Henüz paylaşımı yok.',
              style: TextStyle(fontStyle: FontStyle.italic)),
        ),
      );
    }
    return ListView.separated(
      padding: const EdgeInsets.all(16),
      itemCount: posts.length,
      separatorBuilder: (_, __) => const SizedBox(height: 12),
      itemBuilder: (context, index) => PostCard(
        post: posts[index],
        onTap: () => context.push('/posts/${posts[index].id}', extra: posts[index]),
      ),
    );
  }
}

class _CommentsTab extends StatelessWidget {
  const _CommentsTab({required this.comments});
  final List<MyComment> comments;

  @override
  Widget build(BuildContext context) {
    if (comments.isEmpty) {
      return const Center(
        child: Padding(
          padding: EdgeInsets.all(32),
          child: Text('Henüz yorumu yok.',
              style: TextStyle(fontStyle: FontStyle.italic)),
        ),
      );
    }
    final colorScheme = Theme.of(context).colorScheme;
    final textTheme = Theme.of(context).textTheme;

    return ListView.separated(
      padding: const EdgeInsets.all(16),
      itemCount: comments.length,
      separatorBuilder: (_, __) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final comment = comments[index];
        return Card(
          child: InkWell(
            onTap: () => context.push('/posts/${comment.postId}'),
            borderRadius: BorderRadius.circular(12),
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    comment.postTitle,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.labelMedium?.copyWith(
                      color: colorScheme.primary,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Text(comment.content, style: textTheme.bodyMedium),
                  const SizedBox(height: 12),
                  Row(
                    children: [
                      Icon(Icons.thumb_up_outlined,
                          size: 14, color: colorScheme.onSurfaceVariant),
                      const SizedBox(width: 4),
                      Text('${comment.upvoteCount}', style: textTheme.labelSmall),
                      const SizedBox(width: 12),
                      Icon(Icons.thumb_down_outlined,
                          size: 14, color: colorScheme.onSurfaceVariant),
                      const SizedBox(width: 4),
                      Text('${comment.downvoteCount}', style: textTheme.labelSmall),
                      const Spacer(),
                      Text(
                        comment.createdAgo,
                        style: textTheme.labelSmall?.copyWith(
                          color: colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ],
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

class _UserHeader extends StatelessWidget {
  const _UserHeader({required this.user});
  final AuthUser user;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          children: [
            KararAvatar(
              username: user.username,
              radius: 40,
              fontSize: 32,
            ),
            const SizedBox(height: 16),
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Flexible(
                  child: Text(
                    user.username,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context)
                        .textTheme
                        .headlineSmall
                        ?.copyWith(fontWeight: FontWeight.bold),
                  ),
                ),
                const SizedBox(width: 6),
                KarmaBadge(karma: user.karma, size: 20, showDetail: true),
              ],
            ),
            const SizedBox(height: 8),
            if ((user.bio ?? '').isNotEmpty) ...[
              Text(
                user.bio!,
                textAlign: TextAlign.center,
                maxLines: 4,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.bodyMedium,
              ),
              const SizedBox(height: 12),
            ],
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                _StatItem(label: 'Karma', value: '${user.karma}'),
                const SizedBox(width: 32),
                _StatItem(label: 'Post', value: '${user.postCount}'),
              ],
            ),
            const Divider(height: 32),
            if (user.joinedAt != null)
              Text(
                '${DateFormatter.full(user.joinedAt!)} tarihinden beri üye',
                style: Theme.of(context).textTheme.bodySmall,
              ),
          ],
        ),
      ),
    );
  }
}

class _StatItem extends StatelessWidget {
  const _StatItem({required this.label, required this.value});
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Text(
          value,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(fontSize: 20, fontWeight: FontWeight.bold),
        ),
        Text(
          label,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: Theme.of(context).textTheme.labelSmall,
        ),
      ],
    );
  }
}
