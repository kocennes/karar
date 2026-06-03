import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/theme/app_colors.dart';
import '../../core/history/history_provider.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/post_image.dart';
import '../../shared/widgets/highlight_text.dart';
import 'feed_provider.dart';
import '../report/report_bottom_sheet.dart';
import '../post_detail/vote_bar.dart';

class PostCard extends ConsumerStatefulWidget {
  const PostCard({
    super.key,
    required this.post,
    required this.onTap,
    this.isSeen = false,
    this.isFocused = false,
    this.searchQuery,
    this.onDeleted,
    this.showFirstVoteCoachmark = false,
    this.onDismissFirstVoteCoachmark,
    this.onVote,
  });

  final Post post;
  final VoidCallback onTap;
  final bool isSeen;
  final bool isFocused;
  final String? searchQuery;
  final VoidCallback? onDeleted;
  final bool showFirstVoteCoachmark;
  final VoidCallback? onDismissFirstVoteCoachmark;
  final ValueChanged<VoteType>? onVote;

  @override
  ConsumerState<PostCard> createState() => _PostCardState();
}

class _PostCardState extends ConsumerState<PostCard> {
  bool _revealSensitive = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        ref.read(historyProvider.notifier).trackImpression(widget.post.id);
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final post = widget.post;
    final isSensitive = post.isSensitive && !_revealSensitive;

    return RepaintBoundary(
      child: Semantics(
        label: 'Gönderi: ${post.title}',
        button: true,
        onTapHint: 'Detayları gör',
        child: Opacity(
          opacity: widget.isSeen ? 0.6 : 1.0,
          child: Card(
            shape: widget.isFocused
                ? RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(12),
                    side: BorderSide(
                      color: Theme.of(context).colorScheme.primary,
                      width: 2,
                    ),
                  )
                : null,
            child: InkWell(
              borderRadius: BorderRadius.circular(12),
              onTap: widget.onTap,
              focusColor:
                  Theme.of(context).colorScheme.primary.withValues(alpha: 0.12),
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        post.isAnonymous
                            ? CircleAvatar(
                                radius: 12,
                                backgroundColor: Colors.grey.shade300,
                                child: const Icon(Icons.person_outline,
                                    size: 14, color: Colors.grey),
                              )
                            : KararAvatar(
                                username: post.authorName ?? 'Misafir',
                                radius: 12,
                                fontSize: 10,
                              ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Text(
                            post.isAnonymous
                                ? '@anonim'
                                : '@${post.authorName ?? 'misafir'}',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: Theme.of(context)
                                .textTheme
                                .labelMedium
                                ?.copyWith(
                                  color: post.isAnonymous
                                      ? AppColors.textTertiary
                                      : AppColors.textSecondary,
                                  fontWeight: FontWeight.w700,
                                ),
                          ),
                        ),
                        const Text(' • '),
                        Text(
                          post.createdAgo,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style:
                              Theme.of(context).textTheme.labelSmall?.copyWith(
                                    color: AppColors.textTertiary,
                                  ),
                        ),
                        _MoreMenu(
                          post: post,
                          onDeleted: widget.onDeleted,
                        ),
                      ],
                    ),
                    const SizedBox(height: 8),
                    Text(
                      '${post.category.icon} ${post.category.name}',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.labelSmall?.copyWith(
                            color: AppColors.textTertiary,
                            fontWeight: FontWeight.w600,
                          ),
                    ),
                    if (post.rankingLabel != null) ...[
                      const SizedBox(height: 6),
                      Container(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 6, vertical: 2),
                        decoration: BoxDecoration(
                          color: AppColors.primary.withValues(alpha: 0.08),
                          borderRadius: BorderRadius.circular(4),
                        ),
                        child: Text(
                          post.rankingLabel!,
                          style: TextStyle(
                            fontSize: 10,
                            fontWeight: FontWeight.w700,
                            color: Theme.of(context).colorScheme.primary,
                          ),
                        ),
                      ),
                    ],
                    const SizedBox(height: 8),
                    if (isSensitive)
                      Container(
                        padding: const EdgeInsets.all(12),
                        decoration: BoxDecoration(
                          color: Theme.of(context)
                              .colorScheme
                              .surfaceContainerHighest,
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: Row(
                          children: [
                            const Icon(Icons.warning_amber_rounded,
                                size: 20, color: Colors.orange),
                            const SizedBox(width: 12),
                            const Expanded(
                              child: Text(
                                'Hassas İçerik',
                                style: TextStyle(fontWeight: FontWeight.bold),
                              ),
                            ),
                            TextButton(
                              onPressed: () {
                                setState(() {
                                  _revealSensitive = true;
                                });
                              },
                              child: const Text('GÖSTER'),
                            ),
                          ],
                        ),
                      )
                    else
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Expanded(
                                child: HighlightText(
                                  text: post.title,
                                  highlight: widget.searchQuery ?? '',
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  style: Theme.of(context)
                                      .textTheme
                                      .titleMedium
                                      ?.copyWith(
                                        fontWeight: FontWeight.w800,
                                        height: 1.35,
                                      ),
                                ),
                              ),
                              if (post.hasImage) ...[
                                const SizedBox(width: 12),
                                Stack(
                                  children: [
                                    PostImage(
                                      imageUrl: post.imageUrls.isNotEmpty
                                          ? post.imageUrls.first
                                          : post.imageUrl!,
                                      width: 76,
                                      height: 76,
                                      borderRadius: 10,
                                      memCacheWidth: 200,
                                      heroTag: 'post_image_${post.id}_0',
                                    ),
                                    if (post.imageUrls.length > 1)
                                      Positioned(
                                        right: 4,
                                        bottom: 4,
                                        child: Container(
                                          padding: const EdgeInsets.symmetric(
                                              horizontal: 4, vertical: 2),
                                          decoration: BoxDecoration(
                                            color: Colors.black54,
                                            borderRadius:
                                                BorderRadius.circular(4),
                                          ),
                                          child: Row(
                                            mainAxisSize: MainAxisSize.min,
                                            children: [
                                              const Icon(Icons.copy,
                                                  size: 8, color: Colors.white),
                                              const SizedBox(width: 2),
                                              Text(
                                                '${post.imageUrls.length}',
                                                style: const TextStyle(
                                                  color: Colors.white,
                                                  fontSize: 8,
                                                  fontWeight: FontWeight.bold,
                                                ),
                                              ),
                                            ],
                                          ),
                                        ),
                                      ),
                                  ],
                                ),
                              ],
                            ],
                          ),
                          if (widget.searchQuery != null &&
                              widget.searchQuery!.isNotEmpty &&
                              !post.title.toLowerCase().contains(
                                  widget.searchQuery!.toLowerCase()) &&
                              post.content
                                  .toLowerCase()
                                  .contains(widget.searchQuery!.toLowerCase()))
                            _ContentSnippet(
                              content: post.content,
                              query: widget.searchQuery!,
                            ),
                        ],
                      ),
                    if (post.tags.isNotEmpty) ...[
                      const SizedBox(height: 8),
                      Wrap(
                        spacing: 6,
                        runSpacing: 4,
                        children: post.tags
                            .map((tag) => _TagChip(
                                  tag: tag,
                                  onTap: () => context.push(
                                      '/search?q=${Uri.encodeComponent('#$tag')}'),
                                ))
                            .toList(),
                      ),
                    ],
                    const SizedBox(height: 14),
                    if (widget.showFirstVoteCoachmark)
                      _FirstVoteCoachmark(
                        onDismiss: widget.onDismissFirstVoteCoachmark,
                      ),
                    VoteBar(post: post, isCompact: true),
                    if (widget.onVote != null) ...[
                      const SizedBox(height: 10),
                      Row(
                        children: [
                          Expanded(
                            child: _FeedVoteButton(
                              key: const ValueKey('feed_vote_hakli_button'),
                              label: 'HaklÄ±',
                              icon: Icons.check_circle_outline,
                              color: Theme.of(context).brightness ==
                                      Brightness.dark
                                  ? AppColors.darkHakli
                                  : AppColors.hakli,
                              selected: post.myVote == VoteType.hakli,
                              onPressed: () =>
                                  widget.onVote?.call(VoteType.hakli),
                            ),
                          ),
                          const SizedBox(width: 8),
                          Expanded(
                            child: _FeedVoteButton(
                              key: const ValueKey('feed_vote_haksiz_button'),
                              label: 'HaksÄ±z',
                              icon: Icons.cancel_outlined,
                              color: Theme.of(context).brightness ==
                                      Brightness.dark
                                  ? AppColors.darkHaksiz
                                  : AppColors.haksiz,
                              selected: post.myVote == VoteType.haksiz,
                              onPressed: () =>
                                  widget.onVote?.call(VoteType.haksiz),
                            ),
                          ),
                        ],
                      ),
                    ],
                    const SizedBox(height: 14),
                    Wrap(
                      spacing: 16,
                      runSpacing: 8,
                      children: [
                        _Stat(
                          icon: Icons.check_circle_outline,
                          value: post.voteCountHakli,
                          label: 'Haklı',
                        ),
                        _Stat(
                          icon: Icons.cancel_outlined,
                          value: post.voteCountHaksiz,
                          label: 'Haksız',
                        ),
                        _Stat(
                          icon: Icons.chat_bubble_outline,
                          value: post.commentCount,
                          label: 'Yorum',
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _FirstVoteCoachmark extends StatelessWidget {
  const _FirstVoteCoachmark({this.onDismiss});

  final VoidCallback? onDismiss;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Semantics(
        label: 'Ilk oyunu at ipucu',
        child: Container(
          width: double.infinity,
          padding: const EdgeInsets.fromLTRB(12, 10, 8, 10),
          decoration: BoxDecoration(
            color: colorScheme.primaryContainer,
            borderRadius: BorderRadius.circular(8),
            border: Border.all(
              color: colorScheme.primary.withValues(alpha: 0.18),
            ),
          ),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              Icon(
                Icons.touch_app_outlined,
                size: 20,
                color: colorScheme.onPrimaryContainer,
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Ilk oyunu at',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.labelLarge?.copyWith(
                            color: colorScheme.onPrimaryContainer,
                            fontWeight: FontWeight.w800,
                          ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      'Hakli ya da Haksiz sec.',
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.bodySmall?.copyWith(
                            color: colorScheme.onPrimaryContainer,
                          ),
                    ),
                  ],
                ),
              ),
              IconButton(
                key: const ValueKey('first_vote_coachmark_dismiss'),
                tooltip: 'Kapat',
                constraints: const BoxConstraints(
                  minWidth: 44,
                  minHeight: 44,
                ),
                onPressed: onDismiss,
                icon: const Icon(Icons.close, size: 18),
                color: colorScheme.onPrimaryContainer,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _FeedVoteButton extends StatelessWidget {
  const _FeedVoteButton({
    super.key,
    required this.label,
    required this.icon,
    required this.color,
    required this.selected,
    required this.onPressed,
  });

  final String label;
  final IconData icon;
  final Color color;
  final bool selected;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final foreground = selected ? Colors.white : color;
    return SizedBox(
      height: 44,
      child: OutlinedButton.icon(
        onPressed: onPressed,
        icon: Icon(icon, size: 18),
        label: Text(
          label,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        style: OutlinedButton.styleFrom(
          foregroundColor: foreground,
          backgroundColor: selected ? color : color.withValues(alpha: 0.08),
          side: BorderSide(color: color.withValues(alpha: selected ? 1 : 0.32)),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
          textStyle: Theme.of(context)
              .textTheme
              .labelLarge
              ?.copyWith(fontWeight: FontWeight.w800),
        ),
      ),
    );
  }
}

class _MoreMenu extends ConsumerWidget {
  const _MoreMenu({required this.post, this.onDeleted});

  final Post post;
  final VoidCallback? onDeleted;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return PopupMenuButton<String>(
      tooltip: 'Gönderi seçenekleri',
      icon: const Icon(Icons.more_vert, size: 18),
      padding: EdgeInsets.zero,
      constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
      onSelected: (value) => _handleAction(context, ref, value),
      itemBuilder: (context) => [
        const PopupMenuItem(
          value: 'not_interested',
          child: Row(
            children: [
              Icon(Icons.do_not_disturb_alt_outlined, size: 18),
              SizedBox(width: 8),
              Text('İlgilenmiyorum'),
            ],
          ),
        ),
        const PopupMenuItem(
          value: 'seen_too_much',
          child: Row(
            children: [
              Icon(Icons.visibility_off_outlined, size: 18),
              SizedBox(width: 8),
              Text('Çok sık görüyorum'),
            ],
          ),
        ),
        const PopupMenuItem(
          value: 'low_quality',
          child: Row(
            children: [
              Icon(Icons.thumb_down_alt_outlined, size: 18),
              SizedBox(width: 8),
              Text('Düşük kaliteli'),
            ],
          ),
        ),
        const PopupMenuItem(
          value: 'report',
          child: Row(
            children: [
              Icon(Icons.flag_outlined, size: 18),
              SizedBox(width: 8),
              Text('Şikayet Et'),
            ],
          ),
        ),
        if (post.isOwner)
          const PopupMenuItem(
            value: 'delete',
            child: Row(
              children: [
                Icon(Icons.delete_outline, size: 18, color: Colors.red),
                SizedBox(width: 8),
                Text(
                  'Sil',
                  style: TextStyle(color: Colors.red),
                ),
              ],
            ),
          ),
        if (post.authorId != null && !post.isOwner)
          const PopupMenuItem(
            value: 'block',
            child: Row(
              children: [
                Icon(Icons.block, size: 18, color: Colors.red),
                SizedBox(width: 8),
                Text(
                  'Kullanıcıyı Engelle',
                  style: TextStyle(color: Colors.red),
                ),
              ],
            ),
          ),
      ],
    );
  }

  void _handleAction(BuildContext context, WidgetRef ref, String action) {
    if (action == 'not_interested' ||
        action == 'seen_too_much' ||
        action == 'low_quality') {
      _markNotInterested(context, ref, action);
    } else if (action == 'report') {
      ReportBottomSheet.show(
        context,
        targetType: 'post',
        targetId: post.id,
        repository: ref.read(postRepositoryProvider),
      );
    } else if (action == 'block') {
      _confirmBlock(context, ref);
    } else if (action == 'delete') {
      _confirmDelete(context, ref);
    }
  }

  void _markNotInterested(BuildContext context, WidgetRef ref, String reason) {
    final feedNotifier = ref.read(feedProvider.notifier);
    final postIndex =
        ref.read(feedProvider).posts.indexWhere((p) => p.id == post.id);
    feedNotifier.markNotInterested(post.id, reason: reason);

    String message = 'Artık bu gönderiyi görmeyeceksin.';
    if (reason == 'seen_too_much') {
      message = 'Geri bildirimin için teşekkürler. Akışın güncelleniyor.';
    } else if (reason == 'low_quality') {
      message = 'Düşük kaliteli içerikleri filtrelememize yardımcı oldun.';
    }

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message),
        action: SnackBarAction(
          label: 'Geri Al',
          onPressed: () {
            feedNotifier.undoRemove(post, postIndex == -1 ? null : postIndex);
          },
        ),
      ),
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
            onPressed: () async {
              final messenger = ScaffoldMessenger.of(context);
              Navigator.pop(ctx);
              try {
                await ref.read(postRepositoryProvider).deletePost(post.id);
                ref.read(feedProvider.notifier).removePost(post.id);
                onDeleted?.call();
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

  void _confirmBlock(BuildContext context, WidgetRef ref) {
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
              final userId = post.authorId!;
              Navigator.pop(ctx);
              try {
                await authService.blockUser(userId);
                ref.read(feedProvider.notifier).removePostsByAuthor(userId);

                messenger.showSnackBar(
                  SnackBar(
                    content: Text('@${post.authorName ?? userId} engellendi.'),
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
}

class _Stat extends StatelessWidget {
  const _Stat({
    required this.icon,
    required this.value,
    required this.label,
  });

  final IconData icon;
  final int value;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: '$value $label',
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 18, color: AppColors.textSecondary),
          const SizedBox(width: 4),
          AnimatedSwitcher(
            duration: const Duration(milliseconds: 200),
            transitionBuilder: (child, animation) {
              return SlideTransition(
                position: animation.drive(
                  Tween<Offset>(
                    begin: const Offset(0, 0.5),
                    end: Offset.zero,
                  ).chain(CurveTween(curve: Curves.easeOut)),
                ),
                child: FadeTransition(opacity: animation, child: child),
              );
            },
            child: Text(
              '$value',
              key: ValueKey(value),
              style: Theme.of(
                context,
              ).textTheme.labelLarge?.copyWith(fontWeight: FontWeight.w800),
            ),
          ),
        ],
      ),
    );
  }
}

class _ContentSnippet extends StatelessWidget {
  const _ContentSnippet({required this.content, required this.query});

  final String content;
  final String query;

  static const _contextPad = 60;

  @override
  Widget build(BuildContext context) {
    final lowerContent = content.toLowerCase();
    final lowerQuery = query.toLowerCase();
    final idx = lowerContent.indexOf(lowerQuery);
    if (idx == -1) return const SizedBox.shrink();

    final start = (idx - _contextPad).clamp(0, content.length);
    final end = (idx + query.length + _contextPad).clamp(0, content.length);
    final raw = content.substring(start, end).trim();
    final snippet =
        '${start > 0 ? '…' : ''}$raw${end < content.length ? '…' : ''}';

    return Padding(
      padding: const EdgeInsets.only(top: 4),
      child: HighlightText(
        text: snippet,
        highlight: query,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
        style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: AppColors.textSecondary,
            ),
      ),
    );
  }
}

class _TagChip extends StatelessWidget {
  const _TagChip({required this.tag, required this.onTap});
  final String tag;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 44,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(6),
        child: Align(
          alignment: Alignment.center,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 180),
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
              decoration: BoxDecoration(
                color: AppColors.primary.withValues(alpha: 0.1),
                borderRadius: BorderRadius.circular(6),
              ),
              child: Text(
                '#$tag',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.w600,
                  color: AppColors.primary,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
