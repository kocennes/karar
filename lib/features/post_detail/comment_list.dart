import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karar_pressable.dart';
import '../../shared/widgets/karar_sheet.dart';
import '../../shared/widgets/login_nudge.dart';
import '../../shared/widgets/mention_text.dart';
import '../feed/feed_provider.dart';
import '../report/report_bottom_sheet.dart';
import 'detail_menu.dart';
import 'mention_color.dart';
import 'post_detail_provider.dart';

/// Geometry of the thread (spec §6.2). Every avatar sits centred in a
/// 44 × 44 hit box, so a reply's avatar lines up with its parent's text
/// column.
abstract final class _Thread {
  /// Tile side padding.
  static const double gutter = 16;

  /// Avatar hit box (≥ 44 tap target).
  static const double box = 44;

  /// Gap between the avatar box and the text column.
  static const double gap = 10;

  /// Visual avatar size at level 0 / replies.
  static double avatar(int level) => level == 0 ? 40 : 32;

  /// Left indent of a tile: 0, then 48 (reply avatar under the parent's
  /// text), capped at 96 from level 2 on.
  static double indent(int level) => level <= 0
      ? 0
      : level == 1
          ? 48
          : 96;

  /// Top padding of a tile.
  static const double top = 12;

  /// Inset of the hairline between top-level comments (text column start).
  static const double separatorInset = gutter + box + gap;
}

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
        icon: Icons.chat_bubble_outline_rounded,
      );
    }

    final kc = context.kc;
    return ListView.separated(
      shrinkWrap: shrinkWrap,
      physics: shrinkWrap
          ? const NeverScrollableScrollPhysics()
          : const ClampingScrollPhysics(),
      padding: padding,
      itemCount: comments.length,
      separatorBuilder: (context, index) => Padding(
        padding: const EdgeInsets.only(
          left: _Thread.separatorInset,
          right: _Thread.gutter,
        ),
        child: Divider(height: 1, thickness: 1, color: kc.outlineVariant),
      ),
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
    final tile = _CommentTile(
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
    );
    if (comment.replies.isEmpty) return tile;

    final column = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        tile,
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
    );

    // Replies of level ≥ 2 share the capped indent, so only levels 0 and 1
    // draw a thread line (from below the parent avatar down the replies).
    if (level >= 2) return column;
    return Stack(
      children: [
        column,
        PositionedDirectional(
          start: _Thread.gutter + _Thread.indent(level) + _Thread.box / 2 - 1,
          top: _Thread.top + _Thread.box + 4,
          bottom: 14,
          width: 2,
          child: IgnorePointer(
            child: ExcludeSemantics(
              child: DecoratedBox(
                decoration: BoxDecoration(
                  color: context.kc.outlineVariant,
                  borderRadius: KararRadius.all(1),
                ),
              ),
            ),
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

  void _openProfile(BuildContext context) {
    context.push('/users/${comment.authorName}');
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final kc = context.kc;
    final avatarSize = _Thread.avatar(level);
    final hasName = comment.authorName != null;
    final displayName = hasName ? '@${comment.authorName}' : '@anonim';

    Widget avatar = KararAvatar(
      username: comment.authorName ?? '',
      radius: avatarSize / 2,
      shape: KararAvatarShape.rounded,
      anonymous: !hasName,
    );
    avatar = hasName
        ? KararPressable(
            onTap: () => _openProfile(context),
            semanticLabel: displayName,
            focusRadius: avatarSize * 0.3,
            minSize: const Size(_Thread.box, _Thread.box),
            child: avatar,
          )
        : ExcludeSemantics(
            child: SizedBox.square(
              dimension: _Thread.box,
              child: Center(child: avatar),
            ),
          );

    final nameText = Text(
      displayName,
      maxLines: 1,
      overflow: TextOverflow.ellipsis,
      style: AppTypography.bodyStrong.copyWith(
        fontSize: 14,
        height: 20 / 14,
        fontWeight: FontWeight.w800,
        color: kc.text1,
      ),
    );

    // The highlight box starts 8 px before the avatar box (not at the screen
    // edge), so a highlighted reply keeps its indentation readable.
    const highlightInset = 8.0;
    final inner = Padding(
      padding: const EdgeInsets.fromLTRB(
        highlightInset,
        _Thread.top,
        0,
        6,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          avatar,
          const SizedBox(width: _Thread.gap),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (comment.isPinned)
                  _Overline(
                    icon: Icons.push_pin_rounded,
                    label: 'Sabitlenmiş yorum',
                    color: kc.primaryText,
                  ),
                if (comment.isRising)
                  _Overline(
                    icon: Icons.trending_up_rounded,
                    label: 'Yükselen yorum',
                    color: kc.hakliText,
                  ),
                Padding(
                  padding: const EdgeInsets.only(right: 8),
                  child: Wrap(
                    crossAxisAlignment: WrapCrossAlignment.center,
                    spacing: 6,
                    runSpacing: 2,
                    children: [
                      if (hasName)
                        // The avatar is the accessible profile link; the
                        // name is an extra pointer target.
                        GestureDetector(
                          onTap: () => _openProfile(context),
                          excludeFromSemantics: true,
                          child: ExcludeSemantics(child: nameText),
                        )
                      else
                        nameText,
                      if (comment.isPostOwner) const _OpChip(),
                      // Time sits with the name ("@ad · 1s önce"), so the
                      // action row below never wraps on narrow replies.
                      Text(
                        '· ${comment.createdAgo}',
                        maxLines: 1,
                        style: AppTypography.meta.copyWith(color: kc.text3),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 2),
                Padding(
                  padding: const EdgeInsets.only(right: 8),
                  child: MentionColor(
                    color: kc.primaryText,
                    child: MentionText(
                      text: comment.content,
                      style: AppTypography.body.copyWith(color: kc.text2),
                    ),
                  ),
                ),
                if (comment.isEdited)
                  Padding(
                    padding: const EdgeInsets.only(top: 2),
                    child: Text(
                      'Düzenlendi',
                      style: AppTypography.meta.copyWith(
                        fontSize: 11,
                        fontStyle: FontStyle.italic,
                        fontWeight: FontWeight.w500,
                        color: kc.text3,
                      ),
                    ),
                  ),
                const SizedBox(height: 4),
                _ActionRow(
                  comment: comment,
                  postId: postId,
                  level: level,
                  isPostOwner: isPostOwner,
                  onUpvote: onUpvote,
                  onDownvote: onDownvote,
                  onDelete: onDelete,
                  onReply: onReply,
                  onPin: onPin,
                  onUnpin: onUnpin,
                ),
                if (comment.reactions.values.any((v) => v > 0))
                  Padding(
                    padding: const EdgeInsets.only(right: 8, bottom: 4),
                    child: Wrap(
                      spacing: 4,
                      runSpacing: 0,
                      children: comment.reactions.entries
                          .where((e) => e.value > 0)
                          .map((e) => _ReactionBadge(
                                emoji: e.key,
                                count: e.value,
                                isSelected: comment.myReaction == e.key,
                                onTap: () {
                                  if (comment.myReaction == e.key) {
                                    ref
                                        .read(postDetailProvider(postId)
                                            .notifier)
                                        .removeCommentReaction(comment);
                                  } else {
                                    ref
                                        .read(postDetailProvider(postId)
                                            .notifier)
                                        .reactToComment(comment, e.key);
                                  }
                                },
                              ))
                          .toList(),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );

    return GestureDetector(
      onLongPress: () => _showLongPressMenu(context, ref, comment, onDelete),
      behavior: HitTestBehavior.opaque,
      child: Padding(
        padding: EdgeInsetsDirectional.only(
          start: _Thread.gutter + _Thread.indent(level) - highlightInset,
          end: 8,
        ),
        child: isHighlighted ? _HighlightFade(child: inner) : inner,
      ),
    );
  }
}

/// Pinned / rising overline above the name.
class _Overline extends StatelessWidget {
  const _Overline({
    required this.icon,
    required this.label,
    required this.color,
  });

  final IconData icon;
  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 2),
      child: Row(
        children: [
          Icon(icon, size: 14, color: color),
          const SizedBox(width: 4),
          Flexible(
            child: Text(
              label,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: AppTypography.meta.copyWith(
                fontWeight: FontWeight.w800,
                letterSpacing: 0,
                color: color,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// "Yargılanan" chip next to the post owner's name.
class _OpChip extends StatelessWidget {
  const _OpChip();

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: kc.primaryContainer,
        borderRadius: KararRadius.all(KararRadius.xs),
      ),
      child: Text(
        'Yargılanan',
        style: AppTypography.micro.copyWith(color: kc.onPrimaryContainer),
      ),
    );
  }
}

/// Deep-link highlight: primaryContainer 50 % behind the tile that fades
/// out after 2 s (`reveal`).
class _HighlightFade extends StatefulWidget {
  const _HighlightFade({required this.child});

  final Widget child;

  @override
  State<_HighlightFade> createState() => _HighlightFadeState();
}

class _HighlightFadeState extends State<_HighlightFade>
    with SingleTickerProviderStateMixin {
  static const _hold = Duration(seconds: 2);

  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: _hold + KararMotion.reveal,
  );
  late Animation<double> _opacity;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final fade = KararMotion.fade(context, KararMotion.reveal);
    _controller.duration = _hold + fade;
    final holdWeight = _hold.inMilliseconds.toDouble();
    _opacity = TweenSequence<double>([
      TweenSequenceItem(tween: ConstantTween<double>(1), weight: holdWeight),
      TweenSequenceItem(
        tween: Tween<double>(begin: 1, end: 0).chain(
          CurveTween(curve: KararMotion.standard),
        ),
        weight: fade.inMilliseconds.toDouble().clamp(1, double.infinity),
      ),
    ]).animate(_controller);
    if (!_controller.isAnimating && _controller.value == 0) {
      _controller.forward();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final color = context.kc.primaryContainer.withValues(alpha: 0.5);
    return AnimatedBuilder(
      animation: _opacity,
      builder: (context, child) => DecoratedBox(
        decoration: BoxDecoration(
          color: color.withValues(alpha: color.a * _opacity.value),
          borderRadius: KararRadius.all(KararRadius.input),
        ),
        child: child,
      ),
      child: widget.child,
    );
  }
}

/// Yanıtla · 👍 n · 👎 n · "…" (one merged menu). The time lives in the
/// name row.
class _ActionRow extends ConsumerWidget {
  const _ActionRow({
    required this.comment,
    required this.postId,
    required this.level,
    required this.isPostOwner,
    required this.onUpvote,
    required this.onDownvote,
    required this.onDelete,
    this.onReply,
    this.onPin,
    this.onUnpin,
  });

  final Comment comment;
  final String postId;
  final int level;
  final bool isPostOwner;
  final ValueChanged<Comment> onUpvote;
  final ValueChanged<Comment> onDownvote;
  final ValueChanged<Comment> onDelete;
  final ValueChanged<Comment>? onReply;
  final ValueChanged<Comment>? onPin;
  final VoidCallback? onUnpin;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final items = <Widget>[
      if (onReply != null && !comment.isOwner)
        _ReplyPill(
          onTap: () {
            if (ref.read(currentUserProvider) == null) {
              LoginNudge.show(
                context,
                title: 'Yanıtla',
                message: 'Yorumlara yanıt vermek için giriş yapmalısın.',
                returnTo: '/posts/$postId',
              );
            } else {
              onReply!(comment);
            }
          },
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
    ];

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Wrap(
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 2,
            children: items,
          ),
        ),
        _CommentMoreMenu(
          comment: comment,
          postId: postId,
          level: level,
          isPostOwner: isPostOwner,
          onDelete: onDelete,
          onPin: onPin,
          onUnpin: onUnpin,
        ),
      ],
    );
  }
}

class _ReplyPill extends StatelessWidget {
  const _ReplyPill({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return KararPressable(
      onTap: onTap,
      semanticLabel: 'Yanıtla',
      focusRadius: KararRadius.avatar,
      child: Container(
        constraints: const BoxConstraints(minHeight: 34),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        decoration: BoxDecoration(
          color: kc.muted,
          borderRadius: KararRadius.all(KararRadius.avatar),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.reply_rounded, size: 16, color: kc.text1),
            const SizedBox(width: 6),
            Flexible(
              child: Text(
                'Yanıtla',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: AppTypography.labelS.copyWith(color: kc.text1),
              ),
            ),
          ],
        ),
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
    final kc = context.kc;
    final color = isSelected
        ? (isUpvote ? kc.hakliText : kc.haksizText)
        : kc.text2;

    return KararPressable(
      onTap: onTap,
      selected: isSelected,
      semanticLabel: isUpvote
          ? (isSelected ? 'Beğeniyi kaldır' : 'Beğen')
          : (isSelected ? 'Beğenmemeyi kaldır' : 'Beğenme'),
      semanticValue: count > 0 ? '$count' : null,
      focusRadius: KararRadius.sm,
      pressedScale: 0.9,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              isUpvote
                  ? (isSelected
                      ? Icons.thumb_up_alt_rounded
                      : Icons.thumb_up_alt_outlined)
                  : (isSelected
                      ? Icons.thumb_down_alt_rounded
                      : Icons.thumb_down_alt_outlined),
              size: 18,
              color: color,
            ),
            if (count > 0) ...[
              const SizedBox(width: 5),
              Text(
                '$count',
                style: AppTypography.labelS.copyWith(
                  color: color,
                  fontWeight: isSelected ? FontWeight.w800 : FontWeight.w700,
                  fontFeatures: AppTypography.tabular,
                ),
              ),
            ],
          ],
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
    final kc = context.kc;
    return KararPressable(
      onTap: onTap,
      selected: isSelected,
      semanticLabel: '$emoji $count',
      focusRadius: 14,
      child: Container(
        constraints: const BoxConstraints(minHeight: 28),
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
        decoration: BoxDecoration(
          color: isSelected ? kc.primaryContainer : kc.muted,
          borderRadius: KararRadius.all(14),
          border: Border.all(
            color: isSelected ? kc.primary : kc.muted,
            width: 1.5,
          ),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(emoji, style: const TextStyle(fontSize: 14)),
            const SizedBox(width: 4),
            Text(
              '$count',
              style: AppTypography.meta.copyWith(
                fontWeight: FontWeight.w800,
                letterSpacing: 0,
                color: isSelected ? kc.onPrimaryContainer : kc.text1,
                fontFeatures: AppTypography.tabular,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The single "…" menu of a comment (merges the old two menus and the
/// inline Düzenle / Sil / Sabitle buttons).
class _CommentMoreMenu extends ConsumerWidget {
  const _CommentMoreMenu({
    required this.comment,
    required this.postId,
    required this.level,
    required this.isPostOwner,
    required this.onDelete,
    this.onPin,
    this.onUnpin,
  });

  final Comment comment;
  final String postId;
  final int level;
  final bool isPostOwner;
  final ValueChanged<Comment> onDelete;
  final ValueChanged<Comment>? onPin;
  final VoidCallback? onUnpin;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final kc = context.kc;
    return Builder(
      builder: (anchorContext) => KararPressable(
        tooltip: 'Yorum seçenekleri',
        semanticLabel: 'Yorum seçenekleri',
        focusShape: BoxShape.circle,
        onTap: () async {
          final value = await showAnchoredMenu<String>(
            anchorContext,
            items: _items(context),
          );
          if (value == null || !context.mounted) return;
          _handleAction(context, ref, value);
        },
        child: SizedBox(
          width: 32,
          height: 32,
          child: Icon(Icons.more_horiz_rounded, size: 20, color: kc.text2),
        ),
      ),
    );
  }

  List<PopupMenuEntry<String>> _items(BuildContext context) {
    final canPin = isPostOwner && !comment.isOwner && level == 0;
    return [
      detailMenuItem(
        context,
        value: 'react',
        icon: Icons.add_reaction_outlined,
        label: 'Tepki Ekle',
      ),
      detailMenuItem(
        context,
        value: 'report',
        icon: Icons.flag_outlined,
        label: 'Şikayet et',
      ),
      if (comment.authorId != null && !comment.isOwner)
        detailMenuItem(
          context,
          value: 'block',
          icon: Icons.block_rounded,
          label: 'Engelle',
          destructive: true,
        ),
      if (comment.isOwner) ...[
        detailMenuItem(
          context,
          value: 'edit',
          icon: Icons.edit_outlined,
          label: 'Düzenle',
        ),
        detailMenuItem(
          context,
          value: 'delete',
          icon: Icons.delete_outline_rounded,
          label: 'Sil',
          destructive: true,
        ),
      ],
      if (canPin)
        detailMenuItem(
          context,
          value: 'pin',
          icon: comment.isPinned
              ? Icons.push_pin_rounded
              : Icons.push_pin_outlined,
          label: comment.isPinned ? 'Sabiti Kaldır' : 'Sabitle',
        ),
    ];
  }

  void _handleAction(BuildContext context, WidgetRef ref, String action) {
    switch (action) {
      case 'react':
        _showReactionPicker(context, ref);
      case 'report':
        ReportBottomSheet.show(
          context,
          targetType: 'comment',
          targetId: comment.id,
          repository: ref.read(postRepositoryProvider),
        );
      case 'block':
        _confirmBlock(context, ref);
      case 'edit':
        _showEditComment(context, ref, comment, postId);
      case 'delete':
        _confirmDelete(context, comment, onDelete);
      case 'pin':
        comment.isPinned ? onUnpin?.call() : onPin?.call(comment);
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

    showKararSheet<void>(
      context,
      builder: (ctx) {
        final kc = ctx.kc;
        return SafeArea(
          child: Padding(
            padding: const EdgeInsets.only(bottom: 20),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                KararSheetHeader(
                  title: 'Tepki Seç',
                  onClose: () => Navigator.pop(ctx),
                ),
                const SizedBox(height: 8),
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  child: Wrap(
                    spacing: 12,
                    runSpacing: 12,
                    alignment: WrapAlignment.center,
                    children: emojis
                        .map((e) => KararPressable(
                              onTap: () {
                                Navigator.pop(ctx);
                                _onReact(ref, e);
                              },
                              semanticLabel: e,
                              selected: comment.myReaction == e,
                              focusShape: BoxShape.circle,
                              child: Container(
                                width: 56,
                                height: 56,
                                alignment: Alignment.center,
                                decoration: BoxDecoration(
                                  // Dark: `raised`, since muted is only
                                  // 1.08:1 off the dark sheet.
                                  color: comment.myReaction == e
                                      ? kc.primaryContainer
                                      : kc.isDark
                                          ? kc.raised
                                          : kc.muted,
                                  shape: BoxShape.circle,
                                  border: comment.myReaction == e
                                      ? Border.all(
                                          color: kc.primary,
                                          width: 1.5,
                                        )
                                      : null,
                                ),
                                child: Text(
                                  e,
                                  style: const TextStyle(fontSize: 26),
                                ),
                              ),
                            ))
                        .toList(),
                  ),
                ),
              ],
            ),
          ),
        );
      },
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
            style: FilledButton.styleFrom(
              backgroundColor: ctx.kc.haksizFill,
              foregroundColor: ctx.kc.onColor,
            ),
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

void _showLongPressMenu(
  BuildContext context,
  WidgetRef ref,
  Comment comment,
  ValueChanged<Comment> onDelete,
) {
  KararHaptics.longPress();
  showKararSheet<void>(
    context,
    showDragHandle: true,
    builder: (ctx) {
      final kc = ctx.kc;
      return SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(8, 0, 8, 12),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ListTile(
                leading: const Icon(Icons.copy_rounded),
                title: const Text('Kopyala'),
                onTap: () {
                  Clipboard.setData(ClipboardData(text: comment.content));
                  Navigator.pop(ctx);
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('Yorum kopyalandı')),
                  );
                },
              ),
              ListTile(
                leading: const Icon(Icons.flag_outlined),
                title: const Text('Şikayet Et'),
                onTap: () {
                  Navigator.pop(ctx);
                  ReportBottomSheet.show(
                    context,
                    targetType: 'comment',
                    targetId: comment.id,
                    repository: ref.read(postRepositoryProvider),
                  );
                },
              ),
              if (comment.isOwner)
                ListTile(
                  leading: Icon(
                    Icons.delete_outline_rounded,
                    color: kc.haksizText,
                  ),
                  title: Text('Sil', style: TextStyle(color: kc.haksizText)),
                  onTap: () {
                    Navigator.pop(ctx);
                    _confirmDelete(context, comment, onDelete);
                  },
                ),
            ],
          ),
        ),
      );
    },
  );
}

void _showEditComment(
  BuildContext context,
  WidgetRef ref,
  Comment comment,
  String postId,
) {
  final ctrl = TextEditingController(text: comment.content);
  showKararSheet<void>(
    context,
    scrollControlled: true,
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
        padding: EdgeInsets.only(
          bottom: MediaQuery.of(ctx).viewInsets.bottom + 20,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            KararSheetHeader(
              title: 'Yorumu düzenle',
              onClose: () => Navigator.maybePop(ctx),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 0),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  TextField(
                    controller: ctrl,
                    decoration:
                        const InputDecoration(hintText: 'Yeni yorumun...'),
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
          ],
        ),
      ),
    ),
  ).whenComplete(ctrl.dispose);
}

void _confirmDelete(
  BuildContext context,
  Comment comment,
  ValueChanged<Comment> onDelete,
) {
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
          style: FilledButton.styleFrom(
            backgroundColor: ctx.kc.haksizFill,
            foregroundColor: ctx.kc.onColor,
          ),
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
