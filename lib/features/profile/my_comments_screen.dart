import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/karar_surface.dart';
import '../../shared/widgets/loading_indicator.dart';
import '../../shared/widgets/centered_content.dart';
import 'widgets/column_app_bar.dart';

class MyCommentsScreen extends StatelessWidget {
  const MyCommentsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      appBar: ColumnAppBar(
        title: Text('Yorumlarım'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: MyCommentsList(),
      ),
    );
  }
}

/// The signed-in user's comments (loads `fetchMyComments`), one
/// [ProfileCommentCard] per comment.
///
/// With [sliver] the widget builds a sliver for an enclosing
/// `CustomScrollView` (profile "Yorumlar" tab); otherwise it is a
/// self-contained, pull-to-refresh list (the `/profile/comments` route).
class MyCommentsList extends ConsumerStatefulWidget {
  const MyCommentsList({super.key, this.sliver = false});

  final bool sliver;

  @override
  ConsumerState<MyCommentsList> createState() => _MyCommentsListState();
}

class _MyCommentsListState extends ConsumerState<MyCommentsList> {
  List<MyComment> _comments = [];
  bool _isLoading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (!mounted) return;
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      final comments = await ref.read(postRepositoryProvider).fetchMyComments();
      if (mounted) {
        setState(() {
          _comments = comments;
          _isLoading = false;
        });
      }
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _error = e.friendlyMessage;
          _isLoading = false;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _error = 'Yorumlar yüklenemedi.';
          _isLoading = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final sliver = _buildSliver(context);
    if (widget.sliver) return sliver;
    final scrollView = CustomScrollView(
      physics: const AlwaysScrollableScrollPhysics(),
      slivers: [
        sliver,
        SliverToBoxAdapter(
          child: SizedBox(height: context.navBottomInset + 24),
        ),
      ],
    );
    if (_isLoading || _error != null || _comments.isEmpty) return scrollView;
    return RefreshIndicator(onRefresh: _load, child: scrollView);
  }

  Widget _buildSliver(BuildContext context) {
    if (_isLoading) {
      return const SliverFillRemaining(
        hasScrollBody: false,
        child: Padding(
          padding: EdgeInsets.all(32),
          child: LoadingIndicator(),
        ),
      );
    }
    if (_error != null) {
      return SliverFillRemaining(
        hasScrollBody: false,
        child: ErrorView(message: _error!, onRetry: _load),
      );
    }
    if (_comments.isEmpty) {
      return const SliverFillRemaining(
        hasScrollBody: false,
        child: EmptyState(
          message: 'Henüz yorum yapmadın.',
          icon: Icons.chat_bubble_outline_rounded,
        ),
      );
    }

    return SliverPadding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
      sliver: SliverList.separated(
        itemCount: _comments.length,
        separatorBuilder: (_, __) => const SizedBox(height: 12),
        itemBuilder: (context, index) =>
            ProfileCommentCard(comment: _comments[index]),
      ),
    );
  }
}

/// A comment in a profile list (spec §6.5 `/profile/comments`):
/// `KararSurface(radius 20)` with the post title (13/700 primaryText,
/// one line + chevron), the comment body and a meta row (votes, time,
/// "düzenlendi"). Tapping opens the post.
class ProfileCommentCard extends StatelessWidget {
  const ProfileCommentCard({
    super.key,
    required this.comment,
    this.maxBodyLines = 3,
  });

  final MyComment comment;

  /// Null shows the whole comment.
  final int? maxBodyLines;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final metaStyle = AppTypography.meta.copyWith(
      color: kc.text3,
      fontFeatures: AppTypography.tabular,
    );

    return KararSurface(
      radius: KararRadius.tile,
      padding: const EdgeInsets.fromLTRB(20, 16, 16, 16),
      onTap: () => context.push('/posts/${comment.postId}'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              if (comment.isPinned) ...[
                Icon(Icons.push_pin_rounded, size: 14, color: kc.primaryText),
                const SizedBox(width: 4),
              ],
              Expanded(
                child: Text(
                  comment.postTitle,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.labelS.copyWith(color: kc.primaryText),
                ),
              ),
              const SizedBox(width: 4),
              Icon(Icons.chevron_right_rounded, size: 18, color: kc.text3),
            ],
          ),
          const SizedBox(height: 8),
          Padding(
            padding: const EdgeInsets.only(right: 4),
            child: Text(
              comment.content,
              maxLines: maxBodyLines,
              overflow: maxBodyLines == null ? null : TextOverflow.ellipsis,
              style: AppTypography.body.copyWith(color: kc.text1),
            ),
          ),
          const SizedBox(height: 12),
          Wrap(
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 12,
            runSpacing: 4,
            children: [
              _MetaCount(
                icon: Icons.thumb_up_off_alt_rounded,
                value: comment.upvoteCount,
                style: metaStyle,
                color: kc.text2,
              ),
              _MetaCount(
                icon: Icons.thumb_down_off_alt_rounded,
                value: comment.downvoteCount,
                style: metaStyle,
                color: kc.text2,
              ),
              Text(comment.createdAgo, style: metaStyle),
              // No italic face is bundled (synthetic slant); the meta
              // style (text3, w600) already sets it apart.
              if (comment.isEdited) Text('düzenlendi', style: metaStyle),
            ],
          ),
        ],
      ),
    );
  }
}

class _MetaCount extends StatelessWidget {
  const _MetaCount({
    required this.icon,
    required this.value,
    required this.style,
    required this.color,
  });

  final IconData icon;
  final int value;
  final TextStyle style;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 15, color: color),
        const SizedBox(width: 4),
        Text('$value', style: style.copyWith(color: color)),
      ],
    );
  }
}
