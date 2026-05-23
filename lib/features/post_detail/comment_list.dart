import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/login_nudge.dart';
import '../feed/feed_provider.dart';
import '../report/report_bottom_sheet.dart';
import 'post_detail_provider.dart';
import '../../shared/widgets/mention_text.dart';

class CommentList extends StatelessWidget {
  const CommentList({
    super.key,
    required this.comments,
    required this.postId,
    required this.onUpvote,
    required this.onDownvote,
    required this.onDelete,
    this.onReply,
    this.onPin,
    this.onUnpin,
    this.isPostOwner = false,
    this.shrinkWrap = true,
    this.padding,
    this.highlightedCommentId,
  });

  final List<Comment> comments;
  final String postId;
  final ValueChanged<Comment> onUpvote;
  final ValueChanged<Comment> onDownvote;
  final ValueChanged<Comment> onDelete;
  final ValueChanged<Comment>? onReply;
  final ValueChanged<Comment>? onPin;
  final VoidCallback? onUnpin;
  final bool isPostOwner;
  final bool shrinkWrap;
  final EdgeInsetsGeometry? padding;
  final String? highlightedCommentId;

  @override
  Widget build(BuildContext context) {
    if (comments.isEmpty) {
      return const EmptyState(
        message: 'Henüz yorum yok. İlk yorumu sen yap.',
        icon: Icons.chat_bubble_outline,
      );
    }

    return ListView.builder(
      shrinkWrap: shrinkWrap,
      physics: shrinkWrap
          ? const NeverScrollableScrollPhysics()
          : const ClampingScrollPhysics(),
      padding: padding,
      itemCount: comments.length,
      itemBuilder: (context, index) => RepaintBoundary(
        child: _CommentNode(
          comment: comments[index],
          postId: postId,
          onUpvote: onUpvote,
          onDownvote: onDownvote,
          onDelete: onDelete,
          onReply: onReply,
          onPin: onPin,
          onUnpin: onUnpin,
          isPostOwner: isPostOwner,
          highlightedCommentId: highlightedCommentId,
        ),
      ),
    );
  }
}

class _CommentNode extends StatelessWidget {
  const _CommentNode({
    required this.comment,
    required this.postId,
    required this.onUpvote,
    required this.onDownvote,
    required this.onDelete,
    this.onReply,
    this.onPin,
    this.onUnpin,
    this.isPostOwner = false,
    this.level = 0,
    this.highlightedCommentId,
  });

  final Comment comment;
  final String postId;
  final ValueChanged<Comment> onUpvote;
  final ValueChanged<Comment> onDownvote;
  final ValueChanged<Comment> onDelete;
  final ValueChanged<Comment>? onReply;
  final ValueChanged<Comment>? onPin;
  final VoidCallback? onUnpin;
  final bool isPostOwner;
  final int level;
  final String? highlightedCommentId;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _CommentTile(
          comment: comment,
          postId: postId,
          onUpvote: onUpvote,
          onDownvote: onDownvote,
          onDelete: onDelete,
          onReply: onReply,
          onPin: onPin,
          onUnpin: onUnpin,
          isPostOwner: isPostOwner,
          level: level,
          isHighlighted: comment.id == highlightedCommentId,
        ),
        if (comment.replies.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(top: 2),
            child: Column(
              children: [
                for (final reply in comment.replies)
                  _CommentNode(
                    comment: reply,
                    postId: postId,
                    onUpvote: onUpvote,
                    onDownvote: onDownvote,
                    onDelete: onDelete,
                    onReply: onReply,
                    isPostOwner: isPostOwner,
                    level: level + 1,
                    highlightedCommentId: highlightedCommentId,
                  ),
              ],
            ),
          ),
      ],
    );
  }
}

class _CommentTile extends ConsumerWidget {
  const _CommentTile({
    required this.comment,
    required this.postId,
    required this.onUpvote,
    required this.onDownvote,
    required this.onDelete,
    this.onReply,
    this.onPin,
    this.onUnpin,
    this.isPostOwner = false,
    this.level = 0,
    this.isHighlighted = false,
  });

  final Comment comment;
  final String postId;
  final ValueChanged<Comment> onUpvote;
  final ValueChanged<Comment> onDownvote;
  final ValueChanged<Comment> onDelete;
  final ValueChanged<Comment>? onReply;
  final ValueChanged<Comment>? onPin;
  final VoidCallback? onUnpin;
  final bool isPostOwner;
  final int level;
  final bool isHighlighted;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colorScheme = Theme.of(context).colorScheme;
    final textTheme = Theme.of(context).textTheme;

    // Twitter-like indentation cap for mobile
    final double indent = (level > 3 ? 3 : level) * 16.0;

    return Container(
      margin: EdgeInsets.only(left: indent),
      decoration: BoxDecoration(
        color: isHighlighted
            ? colorScheme.primaryContainer.withValues(alpha: 0.3)
            : null,
        border: level > 0
            ? Border(
                left: BorderSide(
                  color: isHighlighted
                      ? colorScheme.primary
                      : colorScheme.outlineVariant.withValues(alpha: 0.5),
                  width: 1.5,
                ),
              )
            : isHighlighted
                ? Border(
                    left: BorderSide(
                      color: colorScheme.primary,
                      width: 4,
                    ),
                  )
                : null,
      ),
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (comment.isPinned)
            Padding(
              padding: const EdgeInsets.only(bottom: 4),
              child: Row(
                children: [
                  Icon(Icons.push_pin, size: 12, color: colorScheme.primary),
                  const SizedBox(width: 4),
                  Text(
                    'Sabitlenmiş yorum',
                    style: textTheme.labelSmall
                        ?.copyWith(color: colorScheme.primary),
                  ),
                ],
              ),
            ),
          if (comment.isRising)
            Padding(
              padding: const EdgeInsets.only(bottom: 4),
              child: Row(
                children: [
                  const Icon(Icons.trending_up_rounded,
                      size: 12, color: AppColors.hakli),
                  const SizedBox(width: 4),
                  Text(
                    'Yükselen yorum',
                    style: textTheme.labelSmall
                        ?.copyWith(color: AppColors.hakli),
                  ),
                ],
              ),
            ),
          MentionText(text: comment.content, style: textTheme.bodyMedium),
          if (comment.isEdited)
            Padding(
              padding: const EdgeInsets.only(top: 2),
              child: Text(
                'Düzenlendi',
                style: textTheme.labelSmall?.copyWith(
                  color: colorScheme.onSurfaceVariant.withValues(alpha: 0.7),
                  fontStyle: FontStyle.italic,
                  fontSize: 10,
                ),
              ),
            ),
          const SizedBox(height: 8),
          Wrap(
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 8,
            runSpacing: 4,
            children: [
              if (comment.authorName != null) ...[
                GestureDetector(
                  onTap: () => context.push('/users/${comment.authorName}'),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        '@${comment.authorName}',
                        style: textTheme.labelSmall?.copyWith(
                          color: colorScheme.primary,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      if (comment.isPostOwner) ...[
                        const SizedBox(width: 4),
                        Container(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 6,
                            vertical: 1,
                          ),
                          decoration: BoxDecoration(
                            color: colorScheme.primary,
                            borderRadius: BorderRadius.circular(4),
                          ),
                          child: const Text(
                            'Yargılanan',
                            style: TextStyle(
                              color: Colors.white,
                              fontSize: 9,
                              fontWeight: FontWeight.w900,
                            ),
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
                const Text(' · '),
              ],
              Text(
                comment.createdAgo,
                style: textTheme.labelSmall?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
              _CommentMoreMenu(comment: comment, postId: postId),
              if (onReply != null && !comment.isOwner)
                TextButton(
                  onPressed: () {
                    if (ref.read(currentUserProvider) == null) {
                      LoginNudge.show(
                        context,
                        title: 'Yanıtla',
                        message:
                            'Yorumlara yanıt vermek için giriş yapmalısın.',
                        returnTo: '/posts/$postId',
                      );
                    } else {
                      onReply!(comment);
                    }
                  },
                  style: TextButton.styleFrom(
                    minimumSize: const Size(48, 48),
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                    tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                  ),
                  child: const Text('Yanıtla', style: TextStyle(fontSize: 12)),
                ),
              if (comment.isOwner)
                TextButton(
                  onPressed: () => _showEditComment(context, ref),
                  style: TextButton.styleFrom(
                    minimumSize: const Size(48, 48),
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                    tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                  ),
                  child: const Text('Düzenle', style: TextStyle(fontSize: 12)),
                ),
              if (isPostOwner && !comment.isOwner && level == 0)
                TextButton(
                  onPressed: () =>
                      comment.isPinned ? onUnpin?.call() : onPin?.call(comment),
                  style: TextButton.styleFrom(
                    minimumSize: const Size(48, 48),
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                    tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                    foregroundColor: colorScheme.primary,
                  ),
                  child: Text(
                    comment.isPinned ? 'Sabiti Kaldır' : 'Sabitle',
                    style: const TextStyle(fontSize: 12),
                  ),
                ),
              _VoteControl(
                isUpvote: true,
                count: comment.upvoteCount,
                isSelected: comment.myUpvote,
                onTap: () {
                  if (ref.read(currentUserProvider) == null) {
                    LoginNudge.show(
                      context,
                      title: 'Yorumu Beğen',
                      message: 'Yorumları beğenerek destek olmak için giriş yapmalısın.',
                      returnTo: '/posts/$postId',
                    );
                  } else {
                    onUpvote(comment);
                  }
                },
              ),
              _VoteControl(
                isUpvote: false,
                count: comment.downvoteCount,
                isSelected: comment.myDownvote,
                onTap: () {
                  if (ref.read(currentUserProvider) == null) {
                    LoginNudge.show(
                      context,
                      title: 'Yorumu Beğenme',
                      message: 'Fikrini belirtmek için giriş yapmalısın.',
                      returnTo: '/posts/$postId',
                    );
                  } else {
                    onDownvote(comment);
                  }
                },
              ),
              if (comment.isOwner)
                IconButton(
                  onPressed: () => _confirmDelete(context),
                  icon: const Icon(Icons.delete_outline, size: 18),
                  color: colorScheme.error,
                  constraints: const BoxConstraints(
                    minWidth: 48,
                    minHeight: 48,
                  ),
                  tooltip: 'Yorumu sil',
                )
              else
                PopupMenuButton<String>(
                  icon: const Icon(Icons.more_vert, size: 18),
                  onSelected: (value) {
                    if (value == 'report') {
                      ReportBottomSheet.show(
                        context,
                        targetType: 'comment',
                        targetId: comment.id,
                        repository: ref.read(postRepositoryProvider),
                      );
                    }
                  },
                  itemBuilder: (context) => [
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
          if (comment.reactions.isNotEmpty) ...[
            const SizedBox(height: 8),
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: comment.reactions.entries
                  .where((e) => e.value > 0)
                  .map((e) => _ReactionBadge(
                        emoji: e.key,
                        count: e.value,
                        isSelected: comment.myReaction == e.key,
                        onTap: () {
                          if (comment.myReaction == e.key) {
                            ref
                                .read(postDetailProvider(postId).notifier)
                                .removeCommentReaction(comment);
                          } else {
                            ref
                                .read(postDetailProvider(postId).notifier)
                                .reactToComment(comment, e.key);
                          }
                        },
                      ))
                  .toList(),
            ),
          ],
        ],
      ),
    );
  }

  void _showEditComment(BuildContext context, WidgetRef ref) {
    final ctrl = TextEditingController(text: comment.content);
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
          if (ctrl.text == comment.content) {
            Navigator.pop(ctx);
            return;
          }

          final shouldPop = await showDialog<bool>(
            context: ctx,
            builder: (dialogCtx) => AlertDialog(
              title: const Text('Vazgeçilsin mi?'),
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
            20,
            0,
            20,
            MediaQuery.of(ctx).viewInsets.bottom + 20,
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    'Yorumu düzenle',
                    style: Theme.of(ctx)
                        .textTheme
                        .titleMedium
                        ?.copyWith(fontWeight: FontWeight.bold),
                  ),
                  IconButton(
                    tooltip: 'Kapat',
                    onPressed: () => Navigator.maybePop(ctx),
                    icon: const Icon(Icons.close),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              TextField(
                controller: ctrl,
                decoration: const InputDecoration(hintText: 'Yeni yorumun...'),
                maxLines: 4,
                maxLength: 500,
                autofocus: true,
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () {
                  final text = ctrl.text.trim();
                  if (text.length >= 5) {
                    ref
                        .read(postDetailProvider(postId).notifier)
                        .editComment(comment, text);
                    Navigator.pop(ctx);
                  }
                },
                child: const Text('Güncelle'),
              ),
            ],
          ),
        ),
      ),
    ).whenComplete(ctrl.dispose);
  }

  void _confirmDelete(BuildContext context) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Yorumu sil'),
        content: const Text('Bu yorumu silmek istediğine emin misin?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('İptal'),
          ),
          FilledButton(
            onPressed: () {
              Navigator.pop(ctx);
              onDelete(comment);
            },
            child: const Text('Sil'),
          ),
        ],
      ),
    );
  }
}

class _VoteControl extends StatelessWidget {
  const _VoteControl({
    required this.isUpvote,
    required this.count,
    required this.isSelected,
    required this.onTap,
  });

  final bool isUpvote;
  final int count;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final color = isSelected
        ? (isUpvote ? AppColors.hakli : AppColors.haksiz)
        : theme.colorScheme.onSurfaceVariant;

    return Semantics(
      button: true,
      selected: isSelected,
      label: isUpvote
          ? (isSelected ? 'Beğeniyi kaldır' : 'Beğen')
          : (isSelected ? 'Beğenmemeyi kaldır' : 'Beğenme'),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(8),
        child: ConstrainedBox(
          constraints: const BoxConstraints(minWidth: 44, minHeight: 44),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                isUpvote
                    ? (isSelected ? Icons.thumb_up : Icons.thumb_up_outlined)
                    : (isSelected ? Icons.thumb_down : Icons.thumb_down_outlined),
                size: 16,
                color: color,
              ),
              if (count > 0) ...[
                const SizedBox(width: 4),
                Text(
                  '$count',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: color,
                    fontWeight: isSelected ? FontWeight.bold : null,
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _ReactionBadge extends StatelessWidget {
  const _ReactionBadge({
    required this.emoji,
    required this.count,
    required this.isSelected,
    required this.onTap,
  });

  final String emoji;
  final int count;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(20),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
        decoration: BoxDecoration(
          color: isSelected
              ? theme.colorScheme.primary.withValues(alpha: 0.12)
              : theme.colorScheme.surfaceContainerHighest.withValues(alpha: 0.5),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(
            color: isSelected
                ? theme.colorScheme.primary
                : theme.colorScheme.outlineVariant.withValues(alpha: 0.5),
          ),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(emoji, style: const TextStyle(fontSize: 14)),
            const SizedBox(width: 4),
            Text(
              '$count',
              style: theme.textTheme.labelSmall?.copyWith(
                fontWeight: FontWeight.bold,
                color: isSelected ? theme.colorScheme.primary : null,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _CommentMoreMenu extends ConsumerWidget {
  const _CommentMoreMenu({required this.comment, required this.postId});
  final Comment comment;
  final String postId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return PopupMenuButton<String>(
      tooltip: 'Yorum seçenekleri',
      icon: const Icon(Icons.more_horiz, size: 16),
      padding: EdgeInsets.zero,
      constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
      onSelected: (value) => _handleAction(context, ref, value),
      itemBuilder: (context) => [
        const PopupMenuItem(
          value: 'react',
          child: Row(
            children: [
              Icon(Icons.add_reaction_outlined, size: 18),
              SizedBox(width: 8),
              Text('Tepki Ekle', style: TextStyle(fontSize: 13)),
            ],
          ),
        ),
        const PopupMenuItem(
          value: 'report',
          child: Row(
            children: [
              Icon(Icons.flag_outlined, size: 18),
              SizedBox(width: 8),
              Text('Şikayet et', style: TextStyle(fontSize: 13)),
            ],
          ),
        ),
        if (comment.authorId != null && !comment.isOwner)
          const PopupMenuItem(
            value: 'block',
            child: Row(
              children: [
                Icon(Icons.block, size: 18, color: Colors.red),
                SizedBox(width: 8),
                Text(
                  'Engelle',
                  style: TextStyle(fontSize: 13, color: Colors.red),
                ),
              ],
            ),
          ),
      ],
    );
  }

  void _handleAction(BuildContext context, WidgetRef ref, String action) {
    if (action == 'react') {
      _showReactionPicker(context, ref);
    } else if (action == 'report') {
      ReportBottomSheet.show(
        context,
        targetType: 'comment',
        targetId: comment.id,
        repository: ref.read(postRepositoryProvider),
      );
    } else if (action == 'block') {
      _confirmBlock(context, ref);
    }
  }

  void _showReactionPicker(BuildContext context, WidgetRef ref) {
    if (ref.read(currentUserProvider) == null) {
      LoginNudge.show(
        context,
        title: 'Tepki Ver',
        message: 'Yorumlara tepki vermek için giriş yapmalısın.',
        returnTo: '/posts/$postId',
      );
      return;
    }

    final emojis = ['👍', '❤️', '😂', '😮', '😢', '😡', '👏', '🔥'];

    showModalBottomSheet(
      context: context,
      builder: (ctx) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 20, horizontal: 16),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text('Tepki Seç', style: Theme.of(ctx).textTheme.titleMedium),
              const SizedBox(height: 20),
              Wrap(
                spacing: 16,
                runSpacing: 16,
                alignment: WrapAlignment.center,
                children: emojis
                    .map((e) => InkWell(
                          onTap: () {
                            Navigator.pop(ctx);
                            _onReact(ref, e);
                          },
                          borderRadius: BorderRadius.circular(30),
                          child: Container(
                            padding: const EdgeInsets.all(12),
                            decoration: BoxDecoration(
                              color: comment.myReaction == e
                                  ? Theme.of(ctx).colorScheme.primaryContainer
                                  : null,
                              shape: BoxShape.circle,
                            ),
                            child: Text(e, style: const TextStyle(fontSize: 28)),
                          ),
                        ))
                    .toList(),
              ),
              const SizedBox(height: 12),
            ],
          ),
        ),
      ),
    );
  }

  void _onReact(WidgetRef ref, String emoji) {
    if (comment.myReaction == emoji) {
      ref
          .read(postDetailProvider(postId).notifier)
          .removeCommentReaction(comment);
    } else {
      ref
          .read(postDetailProvider(postId).notifier)
          .reactToComment(comment, emoji);
    }
  }

  void _confirmBlock(BuildContext context, WidgetRef ref) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Engelle?'),
        content: const Text('Bu kullanıcının yorumlarını artık görmeyeceksin.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            onPressed: () async {
              Navigator.pop(ctx);
              try {
                await ref
                    .read(authServiceProvider)
                    .blockUser(comment.authorId!);
                ref
                    .read(feedProvider.notifier)
                    .removePostsByAuthor(comment.authorId!);
                if (!context.mounted) return;
                ScaffoldMessenger.of(context).showSnackBar(
                  const SnackBar(content: Text('Kullanıcı engellendi.')),
                );
              } catch (_) {}
            },
            child: const Text('Engelle'),
          ),
        ],
      ),
    );
  }
}
