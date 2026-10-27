import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../../core/api/api_exception.dart';
import '../../core/auth/auth_service.dart';
import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/count_format.dart';
import '../../core/utils/date_formatter.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/content_unavailable_view.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/karar_circle_button.dart';
import '../../shared/widgets/karar_text_tabs.dart';
import '../../shared/widgets/loading_indicator.dart';
import '../feed/feed_provider.dart';
import '../report/report_bottom_sheet.dart';
import 'my_comments_screen.dart';
import 'widgets/profile_header.dart';
import 'widgets/verdict_tile.dart';

class OtherProfileScreen extends ConsumerStatefulWidget {
  const OtherProfileScreen({super.key, required this.username});
  final String username;

  @override
  ConsumerState<OtherProfileScreen> createState() => _OtherProfileScreenState();
}

class _OtherProfileScreenState extends ConsumerState<OtherProfileScreen> {
  AuthUser? _user;
  List<Post> _posts = [];
  List<MyComment> _comments = [];
  bool _isLoading = true;
  String? _error;
  String? _errorCode;
  int _tab = 0;

  @override
  void initState() {
    super.initState();
    _load();
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

  Widget _notFound(BuildContext context) {
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

  @override
  Widget build(BuildContext context) {
    if (_isLoading) {
      return Scaffold(appBar: AppBar(), body: const LoadingIndicator());
    }
    if (_error != null) {
      if (_errorCode == 'USER_NOT_FOUND' || _errorCode == 'HTTP_404') {
        return _notFound(context);
      }
      return Scaffold(
        appBar: AppBar(),
        body: ErrorView(message: _error!, onRetry: _load),
      );
    }
    if (_user == null) return _notFound(context);

    final user = _user!;
    final kc = context.kc;

    return Scaffold(
      appBar: AppBar(
        title: Text(
          '@${user.username}',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        actions: [
          KararCircleButton(
            icon: Icons.block_rounded,
            iconColor: kc.haksizFill,
            tooltip: 'Engelle',
            style: KararCircleStyle.muted,
            size: 40,
            iconSize: 20,
            onPressed: () => _confirmBlock(context, ref, user),
          ),
          const SizedBox(width: 4),
          KararCircleButton(
            icon: Icons.flag_outlined,
            tooltip: 'Şikayet Et',
            style: KararCircleStyle.muted,
            size: 40,
            iconSize: 20,
            onPressed: () {
              ReportBottomSheet.show(
                context,
                targetType: 'user',
                targetId: user.id,
                repository: ref.read(postRepositoryProvider),
              );
            },
          ),
          const SizedBox(width: 4),
        ],
      ),
      body: CenteredContent(
        child: CustomScrollView(
          slivers: [
            SliverToBoxAdapter(
              child: ProfileHeader(
                user: user,
                metaLine: user.joinedAt != null
                    ? '${DateFormatter.full(user.joinedAt!)} tarihinden beri üye'
                    : null,
                stats: [
                  ProfileStat(
                    value: formatCompactCount(user.karma),
                    label: 'Karma',
                  ),
                  ProfileStat(
                    value: formatCompactCount(user.postCount),
                    label: 'Post',
                  ),
                ],
              ),
            ),
            SliverToBoxAdapter(
              child: KararTextTabs(
                labels: [
                  'Postlar (${user.postCount})',
                  'Yorumlar (${user.commentCount})',
                ],
                index: _tab,
                padding: const EdgeInsets.fromLTRB(24, 22, 24, 4),
                onChanged: (i) => setState(() => _tab = i),
              ),
            ),
            if (_tab == 0)
              _PostsTab(posts: _posts)
            else
              _CommentsTab(comments: _comments),
            SliverToBoxAdapter(
              child: SizedBox(height: context.navBottomInset + 24),
            ),
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
            style: FilledButton.styleFrom(
              backgroundColor: ctx.kc.haksizFill,
              foregroundColor: Colors.white,
            ),
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

class _EmptyTab extends StatelessWidget {
  const _EmptyTab(this.message);
  final String message;

  @override
  Widget build(BuildContext context) {
    return SliverToBoxAdapter(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Text(
          message,
          textAlign: TextAlign.center,
          style: AppTypography.body.copyWith(
            fontStyle: FontStyle.italic,
            color: context.kc.text3,
          ),
        ),
      ),
    );
  }
}

class _PostsTab extends StatelessWidget {
  const _PostsTab({required this.posts});
  final List<Post> posts;

  @override
  Widget build(BuildContext context) {
    if (posts.isEmpty) return const _EmptyTab('Henüz paylaşımı yok.');
    return SliverPadding(
      padding: const EdgeInsets.all(16),
      sliver: SliverGrid(
        gridDelegate: VerdictTile.gridDelegate(context),
        delegate: SliverChildBuilderDelegate(
          (context, index) => VerdictTile(
            post: posts[index],
            onTap: () =>
                context.push('/posts/${posts[index].id}', extra: posts[index]),
          ),
          childCount: posts.length,
        ),
      ),
    );
  }
}

class _CommentsTab extends StatelessWidget {
  const _CommentsTab({required this.comments});
  final List<MyComment> comments;

  @override
  Widget build(BuildContext context) {
    if (comments.isEmpty) return const _EmptyTab('Henüz yorumu yok.');
    return SliverPadding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
      sliver: SliverList.separated(
        itemCount: comments.length,
        separatorBuilder: (_, __) => const SizedBox(height: 12),
        itemBuilder: (context, index) => ProfileCommentCard(
          comment: comments[index],
          maxBodyLines: null,
        ),
      ),
    );
  }
}
