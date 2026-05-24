import 'package:confetti/confetti.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/notifications/notification_permission_dialog.dart';
import '../../core/providers.dart';
import '../../core/theme/app_colors.dart';
import '../../core/history/history_provider.dart';
import '../../core/wellbeing/session_vote_tracker.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/content_unavailable_view.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/login_nudge.dart';
import '../../shared/widgets/mention_text.dart';
import '../../shared/widgets/loading_indicator.dart';
import '../../shared/widgets/post_carousel.dart';
import '../../shared/widgets/skeleton.dart';
import '../ads/banner_ad_widget.dart';
import '../feed/feed_provider.dart';
import '../report/report_bottom_sheet.dart';
import 'ai_summary_card.dart';
import 'comment_input.dart';
import 'comment_list.dart';
import 'post_detail_provider.dart';
import 'post_owner_stats_section.dart';
import 'post_poll_widget.dart';
import 'share_picker_sheet.dart';
import 'similar_posts_section.dart';
import 'wellbeing_banner.dart';
import 'vote_bar.dart';

class PostDetailScreen extends ConsumerStatefulWidget {
  const PostDetailScreen({
    super.key,
    required this.postId,
    this.post,
    this.initialCommentId,
  });

  final String postId;
  final Post? post;
  final String? initialCommentId;

  @override
  ConsumerState<PostDetailScreen> createState() => _PostDetailScreenState();
}

class _PostDetailScreenState extends ConsumerState<PostDetailScreen> {
  late final ConfettiController _confettiController;
  final _dwellStopwatch = Stopwatch();
  bool _interacted = false;

  @override
  void initState() {
    super.initState();
    _confettiController =
        ConfettiController(duration: const Duration(seconds: 1));
    _dwellStopwatch.start();

    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(historyProvider.notifier).markAsSeen(widget.postId);
      ref.read(sessionTrackerProvider).incrementPostViewed();
      ref.read(postRepositoryProvider).recordView(widget.postId);

      if (widget.post != null) {
        ref
            .read(postDetailProvider(widget.postId).notifier)
            .setPost(widget.post!);

        ref.read(analyticsServiceProvider).logPostViewed(
              postId: widget.postId,
              category: widget.post!.category.name,
            );
      } else {
        final post = ref.read(postDetailProvider(widget.postId)).post;
        if (post != null) {
          ref.read(analyticsServiceProvider).logPostViewed(
                postId: widget.postId,
                category: post.category.name,
              );
        }
      }
    });
  }

  @override
  void dispose() {
    if (_dwellStopwatch.isRunning) {
      _dwellStopwatch.stop();
      final seconds = _dwellStopwatch.elapsed.inSeconds;
      if (seconds > 2) {
        ref.read(analyticsServiceProvider).logPostDwellTime(
              postId: widget.postId,
              durationSeconds: seconds,
              wasInteracted: _interacted,
            );
        ref.read(postRepositoryProvider).recordView(
              widget.postId,
              dwellSeconds: seconds,
              wasInteracted: _interacted,
            );
      }
    }
    _confettiController.dispose();
    super.dispose();
  }

  void _sharePost(BuildContext context, Post post) {
    setState(() => _interacted = true);
    SharePickerSheet.show(context, post);
  }

  String _postUrl(Post post) => 'https://karar.app/posts/${post.id}';

  Future<void> _copyPostLink(BuildContext context, Post post) async {
    setState(() => _interacted = true);
    await Clipboard.setData(ClipboardData(text: _postUrl(post)));
    if (!context.mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('Post linki kopyalandı.')),
    );
  }

  void _confirmDelete(
      BuildContext context, WidgetRef ref, PostDetailNotifier notifier) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Postu Sil'),
        content: const Text(
          'Bu gönderiyi silmek istediğine emin misin?\nOylar ve yorumlar da silinecek.\nBu işlem geri alınamaz.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('İptal'),
          ),
          FilledButton(
            onPressed: () async {
              final messenger = ScaffoldMessenger.of(context);
              Navigator.pop(ctx);
              final success = await notifier.deletePost();
              if (success) {
                if (context.mounted) {
                  context.go('/');
                  messenger.showSnackBar(
                    const SnackBar(content: Text('Gönderi silindi.')),
                  );
                }
              } else {
                messenger.showSnackBar(
                  const SnackBar(content: Text('Bir hata oluştu.')),
                );
              }
            },
            style: FilledButton.styleFrom(
              backgroundColor: Theme.of(context).colorScheme.error,
            ),
            child: const Text('Evet, Sil'),
          ),
        ],
      ),
    );
  }

  void _confirmBlock(BuildContext context, WidgetRef ref, String userId) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Kullanıcıyı Engelle?'),
        content: const Text(
          'Bu kullanıcının paylaşımlarını artık akışında görmeyeceksin.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            onPressed: () async {
              final messenger = ScaffoldMessenger.of(context);
              final authService = ref.read(authServiceProvider);
              Navigator.pop(ctx);
              try {
                await authService.blockUser(userId);
                if (context.mounted) {
                  ref.read(feedProvider.notifier).removePostsByAuthor(userId);
                  context.pop(); // Go back to feed
                  messenger.showSnackBar(
                    SnackBar(
                      content: const Text('Kullanıcı engellendi.'),
                      action: SnackBarAction(
                        label: 'Geri Al',
                        onPressed: () async {
                          try {
                            await authService.unblockUser(userId);
                            ref.read(feedProvider.notifier).refresh();
                          } catch (_) {}
                        },
                      ),
                    ),
                  );
                }
              } catch (_) {
                messenger.showSnackBar(
                  const SnackBar(content: Text('Bir hata oluştu.')),
                );
              }
            },
            child: const Text('Engelle'),
          ),
        ],
      ),
    );
  }

  void _showEditPost(BuildContext context, WidgetRef ref, Post post,
      PostDetailNotifier notifier) {
    final titleCtrl = TextEditingController(text: post.title);
    final contentCtrl = TextEditingController(text: post.content);

    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      isDismissible: false,
      enableDrag: false,
      builder: (ctx) => PopScope(
        canPop: false,
        onPopInvokedWithResult: (didPop, result) async {
          if (didPop) return;
          if (titleCtrl.text == post.title &&
              contentCtrl.text == post.content) {
            Navigator.pop(ctx);
            return;
          }

          final shouldPop = await showDialog<bool>(
            context: ctx,
            builder: (dialogCtx) => AlertDialog(
              title: const Text('Değişiklikleri Kaydetme?'),
              content: const Text(
                'Düzenlemelerin kaydedilmeyecek. Kapatmak istediğine emin misin?',
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(dialogCtx, false),
                  child: const Text('Hayır'),
                ),
                FilledButton(
                  onPressed: () => Navigator.pop(dialogCtx, true),
                  child: const Text('Evet'),
                ),
              ],
            ),
          );

          if (shouldPop == true && ctx.mounted) {
            Navigator.pop(ctx);
          }
        },
        child: Padding(
          padding: EdgeInsets.fromLTRB(
              20, 0, 20, MediaQuery.of(ctx).viewInsets.bottom + 20),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text('Düzenle',
                      style: Theme.of(ctx)
                          .textTheme
                          .titleLarge
                          ?.copyWith(fontWeight: FontWeight.bold)),
                  IconButton(
                    tooltip: 'Kapat',
                    onPressed: () => Navigator.maybePop(ctx),
                    icon: const Icon(Icons.close),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              TextField(
                controller: titleCtrl,
                decoration: const InputDecoration(labelText: 'Başlık'),
                maxLength: 120,
              ),
              const SizedBox(height: 12),
              TextField(
                controller: contentCtrl,
                decoration: const InputDecoration(labelText: 'İçerik'),
                maxLines: 6,
                maxLength: 1500,
              ),
              const SizedBox(height: 20),
              FilledButton(
                onPressed: () {
                  final t = titleCtrl.text.trim();
                  final c = contentCtrl.text.trim();
                  if (t.length >= 10 && c.length >= 50) {
                    setState(() => _interacted = true);
                    notifier.editPost(t, c);
                    Navigator.pop(ctx);
                  }
                },
                child: const Text('Güncelle'),
              ),
            ],
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(postDetailProvider(widget.postId), (previous, next) {
      if (next.comments.length > (previous?.comments.length ?? 0)) {
        setState(() => _interacted = true);
      }

      if (next.error != null && previous?.error != next.error) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(next.error!),
            backgroundColor: Theme.of(context).colorScheme.error,
            behavior: SnackBarBehavior.floating,
          ),
        );
        // Clear error after showing snackbar to avoid repeated shows on rebuild
        ref.read(postDetailProvider(widget.postId).notifier).clearError();
      }

      if (previous?.post?.myVote != next.post?.myVote &&
          next.post?.myVote != null) {
        setState(() => _interacted = true);
        _confettiController.play();

        final tracker = ref.read(sessionTrackerProvider);
        tracker.incrementVoteCast();
        ref.read(sessionVoteTrackerProvider.notifier).onVoteCast(context);

        // F25: in-app rating logic
        final ratingService = ref.read(ratingServiceProvider);
        ratingService.logVote();
        Future.delayed(const Duration(seconds: 2), () {
          if (context.mounted) ratingService.maybeRequestRating();
        });

        tracker.isFirstVote().then((isFirst) {
          // AHA moment: show pre-dialog on first vote (F13)
          if (isFirst && context.mounted) {
            NotificationPermissionDialog.showIfNeeded(
              context,
              notificationService: ref.read(notificationServiceProvider),
              force: true,
            );
          } else if (!isFirst) {
            ref
                .read(notificationServiceProvider)
                .maybeRequestPermission(force: false);
          }

          if (!isFirst) return;
          tracker.markFirstVoteLogged();
          ref.read(analyticsServiceProvider).logFirstVoteCast(
                voteType: next.post!.myVote!.name,
                timeToVoteSeconds: tracker.elapsedSeconds,
                sessionNumber: tracker.sessionNumber,
              );
        });

        // Conversion nudge for guests
        if (ref.read(currentUserProvider) == null) {
          tracker.shouldShowConversionNudge().then((shouldShow) {
            if (shouldShow && context.mounted) {
              tracker.markNudgeShown();
              LoginNudge.show(
                context,
                title: 'Oy geçmişini kaybetme',
                message:
                    'Bir dahaki sefere uygulamayı açtığında oyların sıfırlanabilir. Hesap açarsan oy ve yorum geçmişin korunur.',
                returnTo: '/posts/${widget.postId}',
                preferRegister: true,
              );
            }
          });
        }
      }
    });

    final detailState = ref.watch(postDetailProvider(widget.postId));
    final post = detailState.post ?? widget.post;
    final notifier = ref.read(postDetailProvider(widget.postId).notifier);

    if (detailState.error != null && post == null) {
      if (detailState.errorCode == 'POST_NOT_FOUND' ||
          detailState.errorCode == 'HTTP_404') {
        return Scaffold(
          appBar: AppBar(title: const Text('İçerik yok')),
          body: ContentUnavailableView(
            icon: Icons.hide_source_outlined,
            title: 'İçerik yok',
            message: 'Bu karar kaldırılmış veya silinmiş.',
            buttonLabel: 'Ana sayfaya dön',
            onPressed: () => context.go('/'),
          ),
        );
      }

      return Scaffold(
        appBar: AppBar(title: const Text('Hata')),
        body: ErrorView(
          message: detailState.error!,
          onRetry: () => notifier.loadPost(),
        ),
      );
    }

    if (post == null) {
      return Scaffold(
        appBar: AppBar(),
        body: const PostDetailSkeleton(),
      );
    }

    if (post.status == 'deleted') {
      return Scaffold(
        appBar: AppBar(title: const Text('İçerik yok')),
        body: ContentUnavailableView(
          icon: Icons.delete_forever_outlined,
          title: 'İçerik yok',
          message: 'Bu karar kaldırılmış veya silinmiş.',
          buttonLabel: 'Ana sayfaya dön',
          onPressed: () => context.go('/'),
        ),
      );
    }

    if (post.status == 'auto_hidden') {
      return Scaffold(
        appBar: AppBar(title: const Text('İçerik gizlendi')),
        body: ContentUnavailableView(
          icon: Icons.visibility_off_outlined,
          title: 'İçerik gizlendi',
          message: 'Bu karar inceleme veya moderasyon nedeniyle gizlenmiş.',
          buttonLabel: 'Ana sayfaya dön',
          onPressed: () => context.go('/'),
        ),
      );
    }

    if (post.status == 'under_review') {
      return Scaffold(
        appBar: AppBar(title: const Text('İncelemede')),
        body: ContentUnavailableView(
          icon: Icons.hourglass_top_outlined,
          title: 'İçerik incelemede',
          message: 'Bu karar moderasyon incelemesi tamamlanana kadar görünmez.',
          buttonLabel: 'Ana sayfaya dön',
          onPressed: () => context.go('/'),
        ),
      );
    }

    final isDesktop = context.isDesktop;

    return Scaffold(
      appBar: AppBar(
        title: const Text('karar'),
        actions: [
          IconButton(
            tooltip: 'Kaydet',
            onPressed: () {
              if (ref.read(currentUserProvider) == null) {
                LoginNudge.show(
                  context,
                  title: 'Gönderiyi Kaydet',
                  message:
                      'Daha sonra tekrar bakmak istediğin gönderileri kaydetmek için giriş yapmalısın.',
                );
              } else {
                notifier.toggleSave();
              }
            },
            icon: Icon(post.isSaved ? Icons.bookmark : Icons.bookmark_border),
          ),
          IconButton(
            tooltip: 'Paylaş',
            onPressed: () => _sharePost(context, post),
            icon: const Icon(Icons.share_outlined),
          ),
          PopupMenuButton<String>(
            icon: const Icon(Icons.more_vert),
            onSelected: (value) {
              setState(() => _interacted = true);
              switch (value) {
                case 'edit':
                  _showEditPost(context, ref, post, notifier);
                  break;
                case 'delete':
                  _confirmDelete(context, ref, notifier);
                  break;
                case 'block':
                  if (post.authorId != null) {
                    _confirmBlock(context, ref, post.authorId!);
                  }
                  break;
                case 'copy':
                  _copyPostLink(context, post);
                  break;
                case 'report':
                  ReportBottomSheet.show(
                    context,
                    targetType: 'post',
                    targetId: post.id,
                    repository: ref.read(postRepositoryProvider),
                  );
                  break;
              }
            },
            itemBuilder: (context) => [
              if (post.isOwner) ...[
                const PopupMenuItem(
                  value: 'edit',
                  child: Row(
                    children: [
                      Icon(Icons.edit_outlined, size: 20),
                      SizedBox(width: 12),
                      Text('Düzenle'),
                    ],
                  ),
                ),
                const PopupMenuItem(
                  value: 'delete',
                  child: Row(
                    children: [
                      Icon(Icons.delete_outline, size: 20, color: Colors.red),
                      SizedBox(width: 12),
                      Text('Sil', style: TextStyle(color: Colors.red)),
                    ],
                  ),
                ),
              ],
              if (!post.isOwner && post.authorId != null)
                const PopupMenuItem(
                  value: 'block',
                  child: Row(
                    children: [
                      Icon(Icons.block_outlined, size: 20, color: Colors.red),
                      SizedBox(width: 12),
                      Text('Engelle', style: TextStyle(color: Colors.red)),
                    ],
                  ),
                ),
              const PopupMenuItem(
                value: 'copy',
                child: Row(
                  children: [
                    Icon(Icons.link_outlined, size: 20),
                    SizedBox(width: 12),
                    Text('Linki kopyala'),
                  ],
                ),
              ),
              const PopupMenuItem(
                value: 'report',
                child: Row(
                  children: [
                    Icon(Icons.flag_outlined, size: 20),
                    SizedBox(width: 12),
                    Text('Şikayet Et'),
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
      body: Stack(
        alignment: Alignment.topCenter,
        children: [
          Column(
            children: [
              const BannerAdWidget(),
              Expanded(
                child: isDesktop
                    ? _DesktopLayout(
                        post: post,
                        detailState: detailState,
                        notifier: notifier,
                        initialCommentId: widget.initialCommentId,
                      )
                    : _MobileLayout(
                        post: post,
                        detailState: detailState,
                        notifier: notifier,
                        initialCommentId: widget.initialCommentId,
                      ),
              ),
            ],
          ),
          ConfettiWidget(
            confettiController: _confettiController,
            blastDirectionality: BlastDirectionality.explosive,
            shouldLoop: false,
            colors: const [
              AppColors.hakli,
              AppColors.haksiz,
              AppColors.accent,
            ],
          ),
        ],
      ),
      bottomSheet: isDesktop
          ? null
          : CommentInput(
              postId: widget.postId,
              onSubmit: notifier.submitComment,
              isLoading: detailState.isSubmittingComment,
            ),
    );
  }
}

class _TopRationaleCard extends StatelessWidget {
  const _TopRationaleCard({required this.comment});
  final Comment comment;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: colorScheme.surfaceContainerHighest.withValues(alpha: 0.3),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
            color: colorScheme.outlineVariant.withValues(alpha: 0.5)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.auto_awesome, size: 14, color: colorScheme.primary),
              const SizedBox(width: 8),
              Text(
                'Öne Çıkan Gerekçe',
                style: theme.textTheme.labelSmall?.copyWith(
                  color: colorScheme.primary,
                  fontWeight: FontWeight.bold,
                  letterSpacing: 0.5,
                ),
              ),
              const Spacer(),
              Icon(Icons.thumb_up, size: 12, color: colorScheme.primary),
              const SizedBox(width: 4),
              Text(
                '${comment.upvoteCount}',
                style: theme.textTheme.labelSmall?.copyWith(
                  fontWeight: FontWeight.bold,
                  color: colorScheme.primary,
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          Text(
            comment.content,
            maxLines: 4,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.bodyMedium?.copyWith(
              fontStyle: FontStyle.italic,
              height: 1.5,
            ),
          ),
          ...[
            const SizedBox(height: 8),
            Text(
              comment.authorName != null ? '@${comment.authorName}' : '@anonim',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: theme.textTheme.labelSmall?.copyWith(
                color: comment.authorName != null
                    ? AppColors.textSecondary
                    : AppColors.textTertiary,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _BalancedRationaleCard extends StatelessWidget {
  const _BalancedRationaleCard({required this.comment});
  final Comment comment;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.hakli.withValues(alpha: 0.05),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.hakli.withValues(alpha: 0.2)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.balance, size: 14, color: AppColors.hakli),
              const SizedBox(width: 8),
              Text(
                'En Dengeli Gerekçe',
                style: theme.textTheme.labelSmall?.copyWith(
                  color: AppColors.hakli,
                  fontWeight: FontWeight.bold,
                  letterSpacing: 0.5,
                ),
              ),
              const Spacer(),
              Tooltip(
                message: 'Hem Haklı hem Haksız oyu verenlerden beğeni aldı.',
                child: Icon(Icons.info_outline,
                    size: 12, color: colorScheme.outline),
              ),
            ],
          ),
          const SizedBox(height: 10),
          Text(
            comment.content,
            maxLines: 4,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.bodyMedium?.copyWith(
              height: 1.5,
            ),
          ),
          ...[
            const SizedBox(height: 8),
            Text(
              comment.authorName != null ? '@${comment.authorName}' : '@anonim',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: theme.textTheme.labelSmall?.copyWith(
                color: comment.authorName != null
                    ? AppColors.textSecondary
                    : AppColors.textTertiary,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
        ],
      ),
    );
  }
}

// ── Desktop two-column layout ────────────────────────────────────────────────

class _DesktopLayout extends StatelessWidget {
  const _DesktopLayout({
    required this.post,
    required this.detailState,
    required this.notifier,
    this.initialCommentId,
  });

  final Post post;
  final PostDetailState detailState;
  final PostDetailNotifier notifier;
  final String? initialCommentId;

  @override
  Widget build(BuildContext context) {
    return CenteredContent(
      maxWidth: 1100,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Sol: post içeriği + oy butonları
          Expanded(
            flex: 5,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(24, 20, 24, 24),
              children: [
                _PostContent(
                  post: post,
                  notifier: notifier,
                  topRationale: detailState.topRationale,
                  balancedRationale: detailState.balancedRationale,
                ),
                const SizedBox(height: 28),
                SimilarPostsSection(postId: post.id),
              ],
            ),
          ),
          const VerticalDivider(width: 1),
          // Sağ: yorum paneli (sabit kaydırma)
          Expanded(
            flex: 4,
            child: Column(
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
                  child: _CommentHeader(
                    count: post.commentCount,
                    selectedSort: detailState.commentSort,
                    onSortChanged: notifier.setCommentSort,
                  ),
                ),
                Expanded(
                  child: detailState.isLoadingComments
                      ? const Center(child: LoadingIndicator())
                      : CommentList(
                          postId: post.id,
                          comments: detailState.comments,
                          onUpvote: notifier.upvoteComment,
                          onDownvote: notifier.downvoteComment,
                          onDelete: notifier.deleteComment,
                          onReply: notifier.setReplyingTo,
                          onPin: notifier.pinComment,
                          onUnpin: notifier.unpinComment,
                          isPostOwner: post.isOwner,
                          shrinkWrap: false,
                          padding: const EdgeInsets.fromLTRB(0, 8, 0, 8),
                          highlightedCommentId: initialCommentId,
                        ),
                ),
                CommentInput(
                  postId: post.id,
                  onSubmit: notifier.submitComment,
                  isLoading: detailState.isSubmittingComment,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

// ── Mobile single-column layout ──────────────────────────────────────────────

class _MobileLayout extends StatelessWidget {
  const _MobileLayout({
    required this.post,
    required this.detailState,
    required this.notifier,
    this.initialCommentId,
  });

  final Post post;
  final PostDetailState detailState;
  final PostDetailNotifier notifier;
  final String? initialCommentId;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
      children: [
        _PostContent(
          post: post,
          notifier: notifier,
          topRationale: detailState.topRationale,
          balancedRationale: detailState.balancedRationale,
        ),
        const SizedBox(height: 28),
        _CommentHeader(
          count: post.commentCount,
          selectedSort: detailState.commentSort,
          onSortChanged: notifier.setCommentSort,
        ),
        const SizedBox(height: 12),
        if (detailState.isLoadingComments)
          const LoadingIndicator()
        else
          CommentList(
            postId: post.id,
            comments: detailState.comments,
            onUpvote: notifier.upvoteComment,
            onDownvote: notifier.downvoteComment,
            onDelete: notifier.deleteComment,
            onReply: notifier.setReplyingTo,
            onPin: notifier.pinComment,
            onUnpin: notifier.unpinComment,
            isPostOwner: post.isOwner,
            highlightedCommentId: initialCommentId,
          ),
        const SizedBox(height: 24),
        SimilarPostsSection(postId: post.id),
        const SizedBox(height: 80),
      ],
    );
  }
}

// ── Ortak post içeriği ───────────────────────────────────────────────────────

class _CommentHeader extends StatelessWidget {
  const _CommentHeader({
    required this.count,
    required this.selectedSort,
    required this.onSortChanged,
  });

  final int count;
  final String selectedSort;
  final ValueChanged<String> onSortChanged;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: Text(
            'Yorumlar ($count)',
            style: Theme.of(context).textTheme.titleMedium?.copyWith(
                  fontWeight: FontWeight.w800,
                ),
          ),
        ),
        PopupMenuButton<String>(
          tooltip: 'Yorum sirala',
          constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
          initialValue: selectedSort,
          onSelected: onSortChanged,
          itemBuilder: (context) => const [
            PopupMenuItem(value: 'top', child: Text('En iyi')),
            PopupMenuItem(value: 'new', child: Text('En yeni')),
            PopupMenuItem(value: 'old', child: Text('En eski')),
            PopupMenuItem(value: 'controversial', child: Text('Tartismali')),
          ],
          child: InputChip(
            avatar: const Icon(Icons.sort, size: 18),
            label: Text(_sortLabel(selectedSort)),
          ),
        ),
      ],
    );
  }

  static String _sortLabel(String sort) => switch (sort) {
        'new' => 'En yeni',
        'old' => 'En eski',
        'controversial' => 'Tartismali',
        _ => 'En iyi',
      };
}

class _PostContent extends ConsumerWidget {
  const _PostContent({
    required this.post,
    required this.notifier,
    this.topRationale,
    this.balancedRationale,
  });

  final Post post;
  final PostDetailNotifier notifier;
  final Comment? topRationale;
  final Comment? balancedRationale;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final detailState = ref.watch(postDetailProvider(post.id));
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (post.status != 'active') ...[
          _ModerationBanner(post: post),
          const SizedBox(height: 16),
        ],
        Row(
          children: [
            if (post.isAnonymous) ...[
              Flexible(
                child: Text(
                  '@anonim',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.labelMedium?.copyWith(
                        color: AppColors.textTertiary,
                        fontWeight: FontWeight.bold,
                      ),
                ),
              ),
              const Text(' · '),
            ] else if (post.authorName != null) ...[
              Flexible(
                child: GestureDetector(
                  onTap: () => context.push('/users/${post.authorName}'),
                  child: Text(
                    '@${post.authorName}',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.labelMedium?.copyWith(
                          color: Theme.of(context).colorScheme.primary,
                          fontWeight: FontWeight.bold,
                        ),
                  ),
                ),
              ),
              const Text(' · '),
            ],
            Flexible(
              child: Text(
                '${post.category.icon} ${post.category.name} · ${post.createdAgo} · ${post.readingTimeMinutes} dk okuma',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.labelMedium?.copyWith(
                      color: AppColors.textSecondary,
                      fontWeight: FontWeight.w700,
                    ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 12),
        MentionText(
          text: post.title,
          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                fontWeight: FontWeight.w900,
                height: 1.25,
              ),
        ),
        const SizedBox(height: 16),
        MentionText(
          text: post.content,
          style: Theme.of(context).textTheme.bodyLarge?.copyWith(height: 1.6),
        ),
        if (post.isEdited)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Text(
              'Düzenlendi',
              style: Theme.of(context).textTheme.labelSmall?.copyWith(
                    color: Theme.of(context)
                        .colorScheme
                        .onSurfaceVariant
                        .withValues(alpha: 0.7),
                    fontStyle: FontStyle.italic,
                  ),
            ),
          ),
        if (post.hasImage) ...[
          const SizedBox(height: 16),
          PostCarousel(
            imageUrls:
                post.imageUrls.isNotEmpty ? post.imageUrls : [post.imageUrl!],
            postId: post.id,
          ),
        ],
        const SizedBox(height: 24),
        WellbeingBanner(content: post.content),
        VoteBar(post: post),
        if (post.totalVotes >= 100) ...[
          const SizedBox(height: 12),
          _VerdictBanner(post: post),
        ],
        if (post.aiSummary != null) ...[
          const SizedBox(height: 16),
          AiSummaryCard(
            summary: post.aiSummary!,
            isRefreshing: detailState.isRefreshingAiSummary,
            onRefresh: notifier.refreshAiSummary,
          ),
        ] else if (post.canHaveAiSummary) ...[
          const SizedBox(height: 16),
          _GenerateAiSummaryPlaceholder(
            isLoading: detailState.isRefreshingAiSummary,
            onGenerate: notifier.refreshAiSummary,
          ),
        ],
        if (topRationale != null) ...[
          const SizedBox(height: 16),
          _TopRationaleCard(comment: topRationale!),
        ],
        if (balancedRationale != null &&
            balancedRationale?.id != topRationale?.id) ...[
          const SizedBox(height: 16),
          _BalancedRationaleCard(comment: balancedRationale!),
        ],
        if (post.poll != null) ...[
          const SizedBox(height: 24),
          PostPollWidget(
            poll: post.poll!,
            onVote: (optionId) {
              if (ref.read(currentUserProvider) == null) {
                LoginNudge.show(
                  context,
                  title: 'Ankete Oy Ver',
                  message: 'Ankete oy vermek için hesap oluşturman gerekiyor.',
                );
                return;
              }
              notifier.votePoll(optionId);
            },
          ),
        ],
        const SizedBox(height: 16),
        if (post.isClosed)
          Container(
            margin: const EdgeInsets.only(bottom: 10),
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
            decoration: BoxDecoration(
              color: Theme.of(context)
                  .colorScheme
                  .surfaceContainerHighest
                  .withValues(alpha: 0.6),
              borderRadius: BorderRadius.circular(8),
            ),
            child: Row(
              children: [
                Icon(
                  Icons.lock_outline_rounded,
                  size: 16,
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
                const SizedBox(width: 8),
                Text(
                  'Oylama kapandı — 7 gün geçti',
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
                        fontWeight: FontWeight.w500,
                      ),
                ),
              ],
            ),
          ),
        Row(
          children: [
            Expanded(
              child: _VoteButton(
                label: 'Haklı',
                icon: Icons.check_circle_outline,
                selected: post.myVote == VoteType.hakli,
                color: AppColors.hakli,
                backgroundColor: AppColors.hakli.withValues(alpha: 0.12),
                onTap: post.isClosed
                    ? null
                    : () {
                        if (ref.read(currentUserProvider) == null) {
                          LoginNudge.show(
                            context,
                            title: 'Oy Ver',
                            message:
                                'Topluluk kararına katılmak ve oylarını kaydetmek için giriş yapmalısın.',
                            returnTo: '/posts/${post.id}',
                          );
                          return;
                        }
                        if (post.myVote == VoteType.hakli) {
                          notifier.removeVote();
                        } else {
                          notifier.vote(VoteType.hakli.name);
                        }
                      },
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: _VoteButton(
                label: 'Haksız',
                icon: Icons.cancel_outlined,
                selected: post.myVote == VoteType.haksiz,
                color: AppColors.haksiz,
                backgroundColor: AppColors.haksiz.withValues(alpha: 0.12),
                onTap: post.isClosed
                    ? null
                    : () {
                        if (ref.read(currentUserProvider) == null) {
                          LoginNudge.show(
                            context,
                            title: 'Oy Ver',
                            message:
                                'Topluluk kararına katılmak ve oylarını kaydetmek için giriş yapmalısın.',
                            returnTo: '/posts/${post.id}',
                          );
                          return;
                        }
                        if (post.myVote == VoteType.haksiz) {
                          notifier.removeVote();
                        } else {
                          notifier.vote(VoteType.haksiz.name);
                        }
                      },
              ),
            ),
          ],
        ),
        if (!post.isOwner)
          Padding(
            padding: const EdgeInsets.only(top: 12),
            child: Align(
              alignment: Alignment.centerRight,
              child: TextButton.icon(
                onPressed: () {
                  ReportBottomSheet.show(
                    context,
                    targetType: 'post',
                    targetId: post.id,
                    repository: ref.read(postRepositoryProvider),
                  );
                },
                icon: const Icon(Icons.flag_outlined, size: 16),
                label: const Text('Bu gönderiyi bildir',
                    style: TextStyle(fontSize: 12)),
                style: TextButton.styleFrom(
                  foregroundColor: Theme.of(context).colorScheme.outline,
                  padding: const EdgeInsets.symmetric(horizontal: 8),
                ),
              ),
            ),
          ),
        if (post.isOwner) ...[
          const SizedBox(height: 20),
          PostOwnerStatsSection(postId: post.id),
        ],
      ],
    );
  }
}

class _GenerateAiSummaryPlaceholder extends StatelessWidget {
  const _GenerateAiSummaryPlaceholder({
    required this.isLoading,
    required this.onGenerate,
  });

  final bool isLoading;
  final VoidCallback onGenerate;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: isLoading ? null : onGenerate,
      borderRadius: BorderRadius.circular(16),
      child: Container(
        width: double.infinity,
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(16),
          border: Border.all(
            color: Theme.of(context).colorScheme.outlineVariant,
            style: BorderStyle.solid,
          ),
        ),
        child: Row(
          children: [
            Icon(
              Icons.auto_awesome,
              size: 20,
              color:
                  Theme.of(context).colorScheme.primary.withValues(alpha: 0.5),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                'Topluluk ne diyor? Yapay zeka ile özetle.',
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                    ),
              ),
            ),
            if (isLoading)
              const SizedBox(
                width: 20,
                height: 20,
                child: CircularProgressIndicator(strokeWidth: 2),
              )
            else
              const Icon(Icons.chevron_right, size: 20),
          ],
        ),
      ),
    );
  }
}

class _VerdictBanner extends StatelessWidget {
  const _VerdictBanner({required this.post});
  final Post post;

  void _showVoteDetails(BuildContext context) {
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Oy Dağılımı'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            _VoteDetailRow(
              label: 'Haklı',
              count: post.voteCountHakli,
              percent: post.hakliPercent,
              color: AppColors.hakli,
            ),
            const SizedBox(height: 16),
            _VoteDetailRow(
              label: 'Haksız',
              count: post.voteCountHaksiz,
              percent: 100 - post.hakliPercent,
              color: AppColors.haksiz,
            ),
            const SizedBox(height: 24),
            Text(
              'Toplam ${post.totalVotes} oy kullanıldı.',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Kapat'),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final isHakli = post.hakliPercent >= 50;
    final winnerLabel = isHakli ? 'Haklı' : 'Haksız';
    final winnerPercent = isHakli ? post.hakliPercent : 100 - post.hakliPercent;
    final winnerColor = isHakli ? AppColors.hakli : AppColors.haksiz;
    final winnerBg = winnerColor.withValues(alpha: 0.12);
    final winnerIcon = isHakli ? '⚖️✅' : '⚖️❌';

    return InkWell(
      onTap: () => _showVoteDetails(context),
      borderRadius: BorderRadius.circular(12),
      child: Container(
        width: double.infinity,
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
        decoration: BoxDecoration(
          color: winnerBg,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: winnerColor.withValues(alpha: 0.4)),
        ),
        child: Row(
          children: [
            Text(winnerIcon, style: const TextStyle(fontSize: 22)),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Topluluk kararı',
                    style: Theme.of(context).textTheme.labelSmall?.copyWith(
                          color: winnerColor,
                          fontWeight: FontWeight.w600,
                          letterSpacing: 0.5,
                        ),
                  ),
                  Text(
                    '$winnerLabel — %$winnerPercent',
                    style: Theme.of(context).textTheme.titleMedium?.copyWith(
                          color: winnerColor,
                          fontWeight: FontWeight.w900,
                        ),
                  ),
                ],
              ),
            ),
            const Icon(Icons.info_outline, size: 16, color: Colors.grey),
            const SizedBox(width: 8),
            Text(
              '${post.totalVotes} oy',
              style: Theme.of(context).textTheme.labelMedium?.copyWith(
                    color: winnerColor.withValues(alpha: 0.8),
                    fontWeight: FontWeight.w600,
                  ),
            ),
          ],
        ),
      ),
    );
  }
}

class _VoteDetailRow extends StatelessWidget {
  const _VoteDetailRow({
    required this.label,
    required this.count,
    required this.percent,
    required this.color,
  });

  final String label;
  final int count;
  final int percent;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              label,
              style: const TextStyle(fontWeight: FontWeight.bold),
            ),
            Text(
              '$count oy (%$percent)',
              style: TextStyle(color: color, fontWeight: FontWeight.bold),
            ),
          ],
        ),
        const SizedBox(height: 8),
        ClipRRect(
          borderRadius: BorderRadius.circular(4),
          child: LinearProgressIndicator(
            value: percent / 100,
            color: color,
            backgroundColor: color.withValues(alpha: 0.1),
            minHeight: 12,
          ),
        ),
      ],
    );
  }
}

class _ModerationBanner extends StatelessWidget {
  const _ModerationBanner({required this.post});
  final Post post;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final isDeleted = post.status == 'deleted';
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: theme.colorScheme.errorContainer,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        children: [
          Icon(
            isDeleted ? Icons.delete_forever : Icons.visibility_off,
            color: theme.colorScheme.onErrorContainer,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  isDeleted ? 'Bu gönderi silinmiş.' : 'Bu gönderi gizlendi.',
                  style: TextStyle(
                    color: theme.colorScheme.onErrorContainer,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                if (post.moderationReason != null)
                  Text(
                    post.moderationReason!,
                    style: TextStyle(
                      color: theme.colorScheme.onErrorContainer,
                      fontSize: 12,
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _VoteButton extends StatelessWidget {
  const _VoteButton({
    required this.label,
    required this.icon,
    required this.selected,
    required this.color,
    required this.backgroundColor,
    this.onTap,
  });

  final String label;
  final IconData icon;
  final bool selected;
  final Color color;
  final Color backgroundColor;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      button: true,
      selected: selected,
      label: selected ? '$label oyunu kaldır' : '$label oyu ver',
      child: FilledButton.icon(
        onPressed: onTap,
        style: FilledButton.styleFrom(
          backgroundColor: selected ? color : backgroundColor,
          foregroundColor: selected ? Colors.white : color,
        ),
        icon: Icon(icon),
        label: Text(label),
      )
          .animate(target: selected ? 1 : 0)
          .scale(
            begin: const Offset(1, 1),
            end: const Offset(1.05, 1.05),
            duration: 150.ms,
            curve: Curves.easeOutBack,
          )
          .shimmer(
            delay: 150.ms,
            duration: 500.ms,
            color: Colors.white.withValues(alpha: 0.2),
          ),
    );
  }
}
