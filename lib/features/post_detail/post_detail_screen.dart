import 'dart:async';

import 'package:confetti/confetti.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/notifications/notification_permission_dialog.dart';
import '../../core/providers.dart';
import '../../core/history/history_provider.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../../core/wellbeing/session_vote_tracker.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/content_unavailable_view.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karar_circle_button.dart';
import '../../shared/widgets/karar_pressable.dart';
import '../../shared/widgets/karar_sheet.dart';
import '../../shared/widgets/karar_surface.dart';
import '../../shared/widgets/login_nudge.dart';
import '../../shared/widgets/mention_text.dart';
import '../../shared/widgets/loading_indicator.dart';
import '../../shared/widgets/on_color_chip.dart';
import '../../shared/widgets/post_carousel.dart';
import '../../shared/widgets/post_surface.dart';
import '../../shared/widgets/skeleton.dart';
import '../ads/banner_ad_widget.dart';
import '../feed/feed_provider.dart';
import '../report/report_bottom_sheet.dart';
import 'ai_summary_card.dart';
import 'comment_input.dart';
import 'comment_list.dart';
import 'detail_menu.dart';
import 'mention_color.dart';
import 'post_detail_provider.dart';
import 'post_owner_stats_section.dart';
import 'post_poll_widget.dart';
import 'share_picker_sheet.dart';
import 'similar_posts_section.dart';
import 'verdict_panel.dart';
import 'wellbeing_banner.dart';

class PostDetailScreen extends ConsumerStatefulWidget {
  const PostDetailScreen({
    super.key,
    required this.postId,
    this.post,
    this.initialCommentId,
    this.referrerCode,
    this.source,
  });

  final String postId;
  final Post? post;
  final String? initialCommentId;
  final String? referrerCode;
  final String? source;

  @override
  ConsumerState<PostDetailScreen> createState() => _PostDetailScreenState();
}

class _PostDetailScreenState extends ConsumerState<PostDetailScreen> {
  late final ConfettiController _confettiController;
  final _dwellStopwatch = Stopwatch();
  bool _interacted = false;
  Timer? _meaningfulDwellTimer;

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

      ref
          .read(judgmentLoopTrackerProvider)
          .onImpression(widget.postId, widget.source ?? 'feed');

      if (widget.post != null) {
        ref
            .read(postDetailProvider(widget.postId).notifier)
            .setPost(widget.post!);

        ref.read(analyticsServiceProvider).logPostViewed(
              postId: widget.postId,
              category: widget.post!.category.name,
              source: widget.source ?? 'feed',
            );
      } else {
        final post = ref.read(postDetailProvider(widget.postId)).post;
        if (post != null) {
          ref.read(analyticsServiceProvider).logPostViewed(
                postId: widget.postId,
                category: post.category.name,
                source: widget.source ?? 'feed',
              );
        }
      }

      if (widget.referrerCode != null) {
        ref.read(analyticsServiceProvider).logShareLandingOpened(
              postId: widget.postId,
              referrerCode: widget.referrerCode,
              isGuest: ref.read(currentUserProvider) == null,
            );
      }

      _meaningfulDwellTimer = Timer(const Duration(seconds: 5), () {
        if (!mounted) return;
        final elapsed = _dwellStopwatch.elapsed.inSeconds;
        ref
            .read(judgmentLoopTrackerProvider)
            .onMeaningfulDwell(widget.postId, elapsed);
        ref.read(analyticsServiceProvider).logMeaningfulDwell(
              postId: widget.postId,
              dwellSeconds: elapsed,
              source: widget.source ?? 'feed',
            );
      });
    });
  }

  @override
  void dispose() {
    _meaningfulDwellTimer?.cancel();
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
              backgroundColor: ctx.kc.haksizFill,
              foregroundColor: Colors.white,
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
            style: FilledButton.styleFrom(
              backgroundColor: ctx.kc.haksizFill,
              foregroundColor: Colors.white,
            ),
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

    showKararSheet<void>(
      context,
      scrollControlled: true,
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
          padding: EdgeInsets.only(
            bottom: MediaQuery.of(ctx).viewInsets.bottom + 20,
          ),
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                KararSheetHeader(
                  title: 'Düzenle',
                  onClose: () => Navigator.maybePop(ctx),
                ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(20, 8, 20, 0),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      TextField(
                        controller: titleCtrl,
                        decoration:
                            const InputDecoration(labelText: 'Başlık'),
                        maxLength: 120,
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: contentCtrl,
                        decoration:
                            const InputDecoration(labelText: 'İçerik'),
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
              ],
            ),
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
            content: Text(
              next.error!,
              style: AppTypography.bodyS.copyWith(
                fontWeight: FontWeight.w600,
                color: Colors.white,
              ),
            ),
            backgroundColor: context.kc.haksizFill,
            behavior: SnackBarBehavior.floating,
          ),
        );
        // Clear error after showing snackbar to avoid repeated shows on rebuild
        ref.read(postDetailProvider(widget.postId).notifier).clearError();
      }

      if (previous?.post?.myVote != next.post?.myVote &&
          next.post?.myVote != null) {
        setState(() => _interacted = true);
        // Confetti is decorative motion: skipped under reduce-motion.
        if (!KararMotion.reduce(context)) _confettiController.play();

        final loopTracker = ref.read(judgmentLoopTrackerProvider);
        loopTracker.onVoted(widget.postId);

        ref.read(analyticsServiceProvider).logVerdictViewed(
              postId: widget.postId,
              voteType: next.post!.myVote!.name,
              source: widget.source ?? 'post_detail',
              rankingReason: next.post?.rankingReason,
            );

        final completedLoop = loopTracker.onVerdictViewed(widget.postId);
        if (completedLoop != null) {
          ref.read(analyticsServiceProvider).logCompletedJudgmentLoop(
                postId: completedLoop.postId,
                source: completedLoop.source,
                dwellSeconds: completedLoop.dwellSeconds,
                loopDurationSeconds: completedLoop.loopDurationSeconds,
                votedBeforeResult: completedLoop.votedBeforeResult,
              );
        }

        if (widget.referrerCode != null) {
          ref.read(analyticsServiceProvider).logShareLandingCompletedJudgment(
                postId: widget.postId,
                voteType: next.post!.myVote!.name,
                referrerCode: widget.referrerCode,
                isGuest: ref.read(currentUserProvider) == null,
              );
        }

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
    final kc = context.kc;
    final canPop = ModalRoute.of(context)?.impliesAppBarDismissal ?? false;

    return Scaffold(
      appBar: AppBar(
        automaticallyImplyLeading: false,
        leadingWidth: 68,
        leading: Padding(
          padding: const EdgeInsets.only(left: 12),
          child: Center(
            child: canPop
                ? KararCircleButton(
                    icon: Icons.arrow_back_ios_new_rounded,
                    iconSize: 20,
                    tooltip: 'Geri',
                    onPressed: () => Navigator.maybePop(context),
                  )
                : KararCircleButton(
                    icon: Icons.home_rounded,
                    tooltip: 'Ana sayfaya dön',
                    onPressed: () => context.go('/'),
                  ),
          ),
        ),
        actions: [
          KararCircleButton(
            icon: post.isSaved
                ? Icons.bookmark_rounded
                : Icons.bookmark_border_rounded,
            iconColor: post.isSaved ? kc.primary : null,
            selected: post.isSaved,
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
          ),
          const SizedBox(width: 8),
          KararCircleButton(
            icon: Icons.send_rounded,
            iconSize: 21,
            tooltip: 'Paylaş',
            onPressed: () => _sharePost(context, post),
          ),
          const SizedBox(width: 8),
          Builder(
            builder: (anchorContext) => KararCircleButton(
              icon: Icons.more_horiz_rounded,
              tooltip: 'Gönderi seçenekleri',
              onPressed: () => _openMoreMenu(anchorContext, post, notifier),
            ),
          ),
          const SizedBox(width: 4),
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
                        referrerCode: widget.referrerCode,
                      )
                    : _MobileLayout(
                        post: post,
                        detailState: detailState,
                        notifier: notifier,
                        initialCommentId: widget.initialCommentId,
                        referrerCode: widget.referrerCode,
                      ),
              ),
              // Composer pinned under the scrolling content (mobile). Kept
              // in the body rather than `Scaffold.bottomSheet` so the theme's
              // rounded sheet shape and the keyboard inset don't apply twice.
              if (!isDesktop)
                CommentInput(
                  postId: widget.postId,
                  onSubmit: notifier.submitComment,
                  isLoading: detailState.isSubmittingComment,
                ),
            ],
          ),
          ConfettiWidget(
            confettiController: _confettiController,
            blastDirectionality: BlastDirectionality.explosive,
            shouldLoop: false,
            colors: KararGradients.confetti,
          ),
        ],
      ),
    );
  }

  Future<void> _openMoreMenu(
    BuildContext anchorContext,
    Post post,
    PostDetailNotifier notifier,
  ) async {
    final value = await showAnchoredMenu<String>(
      anchorContext,
      items: [
        if (post.isOwner) ...[
          detailMenuItem(
            anchorContext,
            value: 'edit',
            icon: Icons.edit_outlined,
            label: 'Düzenle',
          ),
          detailMenuItem(
            anchorContext,
            value: 'delete',
            icon: Icons.delete_outline_rounded,
            label: 'Sil',
            destructive: true,
          ),
        ],
        if (!post.isOwner && post.authorId != null)
          detailMenuItem(
            anchorContext,
            value: 'block',
            icon: Icons.block_rounded,
            label: 'Engelle',
            destructive: true,
          ),
        detailMenuItem(
          anchorContext,
          value: 'copy',
          icon: Icons.link_rounded,
          label: 'Linki kopyala',
        ),
        detailMenuItem(
          anchorContext,
          value: 'report',
          icon: Icons.flag_outlined,
          label: 'Şikayet Et',
        ),
      ],
    );
    if (value == null || !mounted) return;
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
  }
}

// ── Hero card ────────────────────────────────────────────────────────────────

/// Gradient hero (spec §6.1.2): category + source chips, big white title,
/// author row, tags. Photo posts use the gradient too — their photos are
/// shown uncropped in the carousel below.
class _DetailHero extends StatelessWidget {
  const _DetailHero({required this.post});

  final Post post;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final style = CategoryStyle.of(post.category);
    const white = Colors.white;

    return PostSurface(
      post: post,
      showPhoto: false,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 220),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 18, 20, 20),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: [
                      OnColorChip(
                        icon: style.icon,
                        label: post.category.name,
                      ),
                      if (post.contentSource != 'user')
                        OnColorChip(
                          label: post.contentSource == 'ai'
                              ? 'AI destekli'
                              : 'Sistem',
                          dotColor: kc.gold,
                        ),
                    ],
                  ),
                  const SizedBox(height: 14),
                  Semantics(
                    header: true,
                    child: MentionColor(
                      color: white,
                      child: MentionText(
                        text: post.title,
                        style: AppTypography.titleXL.copyWith(color: white),
                      ),
                    ),
                  ),
                ],
              ),
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const SizedBox(height: 14),
                  _HeroAuthorRow(post: post),
                  if (post.tags.isNotEmpty) ...[
                    const SizedBox(height: 6),
                    Wrap(
                      spacing: 8,
                      children: [
                        for (final tag in post.tags)
                          OnColorChip(
                            label: '#$tag',
                            onTap: () => context.push(
                              '/search?q=${Uri.encodeComponent('#$tag')}',
                            ),
                          ),
                      ],
                    ),
                  ],
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _HeroAuthorRow extends StatelessWidget {
  const _HeroAuthorRow({required this.post});

  final Post post;

  @override
  Widget build(BuildContext context) {
    const white = Colors.white;
    final meta = '${post.createdAgo} · ${post.readingTimeMinutes} dk okuma';
    final metaStyle = AppTypography.labelS.copyWith(
      fontWeight: FontWeight.w500,
      color: white,
    );
    final nameStyle = AppTypography.bodyStrong.copyWith(color: white);

    Widget? identity;
    if (post.isAnonymous) {
      identity = Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const KararAvatar(
            username: '',
            radius: 16,
            anonymous: true,
            onColor: true,
          ),
          const SizedBox(width: 8),
          Flexible(
            child: Text(
              '@anonim',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: nameStyle,
            ),
          ),
        ],
      );
    } else if (post.authorName != null) {
      final name = post.authorName!;
      identity = KararPressable(
        onTap: () => context.push('/users/$name'),
        semanticLabel: '@$name',
        focusRadius: 16,
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            KararAvatar(username: name, radius: 16),
            const SizedBox(width: 8),
            Flexible(
              child: Text(
                '@$name',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: nameStyle,
              ),
            ),
          ],
        ),
      );
    }

    return Wrap(
      crossAxisAlignment: WrapCrossAlignment.center,
      runSpacing: 2,
      children: [
        if (identity != null) identity,
        ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 32),
          child: Align(
            widthFactor: 1,
            heightFactor: 1,
            child: Text(
              identity == null ? meta : ' · $meta',
              style: metaStyle,
            ),
          ),
        ),
      ],
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
    this.referrerCode,
  });

  final Post post;
  final PostDetailState detailState;
  final PostDetailNotifier notifier;
  final String? initialCommentId;
  final String? referrerCode;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return CenteredContent(
      maxWidth: 1120,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // Sol: post içeriği + oy paneli
          Expanded(
            flex: 10,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(24, 12, 24, 32),
              children: [
                _PostContent(
                  post: post,
                  notifier: notifier,
                  topRationale: detailState.topRationale,
                  balancedRationale: detailState.balancedRationale,
                  referrerCode: referrerCode,
                  gutter: 0,
                ),
                const SizedBox(height: 32),
                SimilarPostsSection(postId: post.id),
              ],
            ),
          ),
          const SizedBox(width: 8),
          // Sağ: yorum paneli (sabit kaydırma)
          Expanded(
            flex: 7,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(0, 16, 24, 16),
              child: KararSurface(
                radius: KararRadius.card,
                padding: EdgeInsets.zero,
                child: Column(
                  children: [
                    Padding(
                      padding: const EdgeInsets.fromLTRB(20, 14, 12, 10),
                      child: _CommentHeader(
                        count: post.commentCount,
                        selectedSort: detailState.commentSort,
                        onSortChanged: notifier.setCommentSort,
                      ),
                    ),
                    Divider(height: 1, thickness: 1, color: kc.outlineVariant),
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
    this.referrerCode,
  });

  final Post post;
  final PostDetailState detailState;
  final PostDetailNotifier notifier;
  final String? initialCommentId;
  final String? referrerCode;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(0, 4, 0, 32),
      children: [
        _PostContent(
          post: post,
          notifier: notifier,
          topRationale: detailState.topRationale,
          balancedRationale: detailState.balancedRationale,
          referrerCode: referrerCode,
        ),
        const SizedBox(height: 32),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: _CommentHeader(
            count: post.commentCount,
            selectedSort: detailState.commentSort,
            onSortChanged: notifier.setCommentSort,
          ),
        ),
        const SizedBox(height: 4),
        if (detailState.isLoadingComments)
          const Padding(
            padding: EdgeInsets.all(24),
            child: LoadingIndicator(),
          )
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
            padding: EdgeInsets.zero,
            highlightedCommentId: initialCommentId,
          ),
        const SizedBox(height: 28),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: SimilarPostsSection(postId: post.id),
        ),
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
    final kc = context.kc;
    final label = _sortLabel(selectedSort);
    return Row(
      children: [
        Expanded(
          child: Semantics(
            header: true,
            child: Text(
              'Yorumlar ($count)',
              style: AppTypography.headline.copyWith(
                color: kc.text1,
                fontFeatures: AppTypography.tabular,
              ),
            ),
          ),
        ),
        const SizedBox(width: 8),
        Builder(
          builder: (anchorContext) => KararPressable(
            tooltip: 'Yorum sırala',
            semanticLabel: 'Yorum sırala',
            semanticValue: label,
            focusRadius: 20,
            onTap: () async {
              final value = await showAnchoredMenu<String>(
                anchorContext,
                initialValue: selectedSort,
                items: const [
                  PopupMenuItem(value: 'top', child: Text('En iyi')),
                  PopupMenuItem(value: 'new', child: Text('En yeni')),
                  PopupMenuItem(value: 'old', child: Text('En eski')),
                  PopupMenuItem(
                    value: 'controversial',
                    child: Text('Tartışmalı'),
                  ),
                ],
              );
              if (value != null) onSortChanged(value);
            },
            child: Container(
              constraints: const BoxConstraints(minHeight: 40),
              padding: const EdgeInsets.fromLTRB(12, 8, 14, 8),
              decoration: BoxDecoration(
                color: kc.muted,
                borderRadius: KararRadius.all(20),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(Icons.sort_rounded, size: 18, color: kc.text2),
                  const SizedBox(width: 6),
                  Text(
                    label,
                    style: AppTypography.labelS.copyWith(color: kc.text1),
                  ),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }

  static String _sortLabel(String sort) => switch (sort) {
        'new' => 'En yeni',
        'old' => 'En eski',
        'controversial' => 'Tartışmalı',
        _ => 'En iyi',
      };
}

class _PostContent extends ConsumerWidget {
  const _PostContent({
    required this.post,
    required this.notifier,
    this.topRationale,
    this.balancedRationale,
    this.referrerCode,
    this.gutter = 16,
  });

  final Post post;
  final PostDetailNotifier notifier;
  final Comment? topRationale;
  final Comment? balancedRationale;
  final String? referrerCode;

  /// Horizontal page gutter (16 on mobile; 0 when the parent pads).
  final double gutter;

  /// Haklı / Haksız tap from the verdict panel (unchanged vote flow:
  /// share-landing analytics, guest nudge with returnTo, vote / remove).
  void _onVoteTap(BuildContext context, WidgetRef ref, VoteType type) {
    if (referrerCode != null) {
      ref.read(analyticsServiceProvider).logShareLandingVoteAttempt(
            postId: post.id,
            referrerCode: referrerCode,
            isGuest: ref.read(currentUserProvider) == null,
          );
    }
    if (ref.read(currentUserProvider) == null) {
      final returnTo = referrerCode == null
          ? '/posts/${post.id}'
          : '/posts/${post.id}?ref=${Uri.encodeComponent(referrerCode!)}';
      LoginNudge.show(
        context,
        title: 'Oy Ver',
        message:
            'Topluluk kararına katılmak ve oylarını kaydetmek için giriş yapmalısın.',
        returnTo: returnTo,
      );
      return;
    }
    if (post.myVote == type) {
      notifier.removeVote();
    } else {
      notifier.vote(type.name);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final detailState = ref.watch(postDetailProvider(post.id));
    final kc = context.kc;
    final g = gutter;
    EdgeInsets section(double top) => EdgeInsets.fromLTRB(g, top, g, 0);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (post.status != 'active')
          Padding(
            padding: EdgeInsets.fromLTRB(g, 4, g, 12),
            child: _ModerationBanner(post: post),
          ),
        Padding(
          padding: section(4),
          child: _DetailHero(post: post),
        ),
        Padding(
          padding: EdgeInsets.fromLTRB(g + 4, 18, g + 4, 0),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              MentionColor(
                color: kc.primaryText,
                child: MentionText(
                  text: post.content,
                  style: AppTypography.bodyL.copyWith(color: kc.text1),
                ),
              ),
              if (post.isEdited)
                Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Text(
                    'Düzenlendi',
                    style: AppTypography.meta.copyWith(
                      fontStyle: FontStyle.italic,
                      fontWeight: FontWeight.w500,
                      color: kc.text3,
                    ),
                  ),
                ),
            ],
          ),
        ),
        if (post.hasImage)
          Padding(
            padding: section(16),
            child: DecoratedBox(
              decoration: BoxDecoration(
                color: kc.muted,
                borderRadius: KararRadius.all(KararRadius.surface),
              ),
              child: PostCarousel(
                imageUrls: post.imageUrls.isNotEmpty
                    ? post.imageUrls
                    : [post.imageUrl!],
                postId: post.id,
                borderRadius: KararRadius.surface,
              ),
            ),
          ),
        Padding(
          padding: EdgeInsets.symmetric(horizontal: g),
          child: WellbeingBanner(content: post.content),
        ),
        Padding(
          padding: section(20),
          child: VerdictPanel(
            post: post,
            onVote: post.isClosed
                ? null
                : (type) => _onVoteTap(context, ref, type),
          ),
        ),
        if (!post.isOwner)
          Padding(
            padding: const EdgeInsets.only(top: 6),
            child: Center(
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
                label: const Text('Bu gönderiyi bildir'),
                style: TextButton.styleFrom(
                  foregroundColor: kc.text3,
                  iconColor: kc.text3,
                  textStyle: AppTypography.labelS.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
            ),
          ),
        if (post.aiSummary != null)
          Padding(
            padding: section(16),
            child: AiSummaryCard(
              summary: post.aiSummary!,
              isRefreshing: detailState.isRefreshingAiSummary,
              onRefresh: notifier.refreshAiSummary,
            ),
          )
        else if (post.canHaveAiSummary)
          Padding(
            padding: section(16),
            child: _GenerateAiSummaryPlaceholder(
              isLoading: detailState.isRefreshingAiSummary,
              onGenerate: notifier.refreshAiSummary,
            ),
          ),
        if (topRationale != null)
          Padding(
            padding: section(12),
            child: _TopRationaleCard(comment: topRationale!),
          ),
        if (balancedRationale != null &&
            balancedRationale?.id != topRationale?.id &&
            post.totalVotes >= 40)
          Padding(
            padding: section(12),
            child: _BalancedRationaleCard(comment: balancedRationale!),
          ),
        if (post.poll != null)
          Padding(
            padding: section(24),
            child: PostPollWidget(
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
          ),
        if (post.isOwner)
          Padding(
            padding: section(20),
            child: PostOwnerStatsSection(postId: post.id),
          ),
      ],
    );
  }
}

// ── Gerekçe kartları ─────────────────────────────────────────────────────────

class _RationaleCard extends StatelessWidget {
  const _RationaleCard({
    required this.comment,
    required this.icon,
    required this.title,
    required this.accent,
    this.trailing,
    this.italic = false,
  });

  final Comment comment;
  final IconData icon;
  final String title;
  final Color accent;
  final Widget? trailing;
  final bool italic;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return KararSurface(
      padding: const EdgeInsets.fromLTRB(18, 14, 14, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 32),
            child: Row(
              children: [
                Icon(icon, size: 16, color: accent),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    title,
                    style: AppTypography.meta.copyWith(
                      fontWeight: FontWeight.w800,
                      letterSpacing: 0.2,
                      color: accent,
                    ),
                  ),
                ),
                if (trailing != null) trailing!,
              ],
            ),
          ),
          const SizedBox(height: 6),
          Text(
            comment.content,
            maxLines: 4,
            overflow: TextOverflow.ellipsis,
            style: AppTypography.body.copyWith(
              fontStyle: italic ? FontStyle.italic : null,
              color: kc.text1,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            comment.authorName != null ? '@${comment.authorName}' : '@anonim',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: AppTypography.labelS.copyWith(color: kc.text2),
          ),
        ],
      ),
    );
  }
}

class _TopRationaleCard extends StatelessWidget {
  const _TopRationaleCard({required this.comment});
  final Comment comment;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return _RationaleCard(
      comment: comment,
      icon: Icons.auto_awesome_rounded,
      title: 'Öne Çıkan Gerekçe',
      accent: kc.primaryText,
      italic: true,
      trailing: Container(
        padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 4),
        decoration: BoxDecoration(
          color: kc.primaryContainer,
          borderRadius: KararRadius.all(KararRadius.pill),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.thumb_up_alt_rounded,
              size: 13,
              color: kc.onPrimaryContainer,
            ),
            const SizedBox(width: 4),
            Text(
              '${comment.upvoteCount}',
              style: AppTypography.meta.copyWith(
                fontWeight: FontWeight.w800,
                color: kc.onPrimaryContainer,
                fontFeatures: AppTypography.tabular,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _BalancedRationaleCard extends StatelessWidget {
  const _BalancedRationaleCard({required this.comment});
  final Comment comment;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return _RationaleCard(
      comment: comment,
      icon: Icons.balance_rounded,
      title: 'En Dengeli Gerekçe',
      accent: kc.hakliText,
      trailing: Tooltip(
        message: 'Hem Haklı hem Haksız oyu verenlerden beğeni aldı.',
        child: SizedBox(
          width: 32,
          height: 32,
          child: Icon(Icons.info_outline_rounded, size: 16, color: kc.text3),
        ),
      ),
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
    final kc = context.kc;
    final shape = RoundedRectangleBorder(
      borderRadius: KararRadius.all(KararRadius.surface),
      side: BorderSide(color: kc.outlineVariant, width: 1.5),
    );
    return Material(
      color: Colors.transparent,
      shape: shape,
      child: InkWell(
        onTap: isLoading ? null : onGenerate,
        customBorder: shape,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 64),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(18, 14, 14, 14),
            child: Row(
              children: [
                Icon(Icons.auto_awesome_rounded, size: 20, color: kc.primary),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    'Topluluk ne diyor? Yapay zeka ile özetle.',
                    style: AppTypography.bodyS.copyWith(color: kc.text2),
                  ),
                ),
                const SizedBox(width: 8),
                if (isLoading)
                  SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: kc.primary,
                    ),
                  )
                else
                  Icon(Icons.chevron_right_rounded, size: 22, color: kc.text3),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _ModerationBanner extends StatelessWidget {
  const _ModerationBanner({required this.post});
  final Post post;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final isDeleted = post.status == 'deleted';
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: scheme.errorContainer,
        borderRadius: KararRadius.all(KararRadius.tile),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            isDeleted
                ? Icons.delete_forever_rounded
                : Icons.visibility_off_rounded,
            color: scheme.onErrorContainer,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  isDeleted ? 'Bu gönderi silinmiş.' : 'Bu gönderi gizlendi.',
                  style: AppTypography.bodyStrong.copyWith(
                    color: scheme.onErrorContainer,
                  ),
                ),
                if (post.moderationReason != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 2),
                    child: Text(
                      post.moderationReason!,
                      style: AppTypography.bodyS.copyWith(
                        color: scheme.onErrorContainer,
                      ),
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
