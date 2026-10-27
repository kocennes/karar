import 'dart:math' as math;

import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/history/history_provider.dart';
import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../../core/utils/count_format.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/feedback_reason_bottom_sheet.dart';
import '../../shared/widgets/highlight_text.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karar_circle_button.dart';
import '../../shared/widgets/karar_pressable.dart';
import '../../shared/widgets/karar_surface.dart';
import '../../shared/widgets/login_nudge.dart';
import '../../shared/widgets/on_color_chip.dart';
import '../../shared/widgets/post_surface.dart';
import '../../shared/widgets/verdict_chip.dart';
import '../../shared/widgets/vote_rail.dart';
import '../post_detail/share_picker_sheet.dart';
import '../report/report_bottom_sheet.dart';
import 'feed_provider.dart';

/// Visual variants of [PostCard] (spec §5.4).
enum PostCardVariant {
  /// Feed card: full-bleed category gradient (text) or photo, big white
  /// type and the glass vote rail.
  feature,

  /// Surface row for lists (search results, Keşfet sections, similar posts):
  /// category tile, title, verdict chip, comment count.
  compact,
}

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
    this.variant = PostCardVariant.feature,
    this.showCategory = true,
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
  final PostCardVariant variant;

  /// Feature variant: show the category chip in the header. Off on a
  /// category page, where every card is in the same category.
  final bool showCategory;

  @override
  ConsumerState<PostCard> createState() => _PostCardState();
}

class _PostCardState extends ConsumerState<PostCard> {
  bool _revealSensitive = false;
  bool _hovered = false;

  /// Optimistic saved state while a save/unsave request is in flight
  /// (null = follow `post.isSaved`).
  bool? _savedOverride;
  bool _saving = false;

  bool get _isSaved => _savedOverride ?? widget.post.isSaved;

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
  void didUpdateWidget(PostCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.post.id != widget.post.id ||
        oldWidget.post.isSaved != widget.post.isSaved) {
      if (!_saving) _savedOverride = null;
    }
  }

  Future<void> _toggleSave() async {
    if (ref.read(currentUserProvider) == null) {
      LoginNudge.show(
        context,
        title: 'Gönderiyi Kaydet',
        message:
            'Daha sonra tekrar bakmak istediğin gönderileri kaydetmek için giriş yapmalısın.',
      );
      return;
    }
    if (_saving) return;

    final post = widget.post;
    final next = !_isSaved;
    final messenger = ScaffoldMessenger.of(context);
    setState(() {
      _savedOverride = next;
      _saving = true;
    });
    try {
      final repository = ref.read(postRepositoryProvider);
      if (next) {
        await repository.savePost(post.id);
      } else {
        await repository.unsavePost(post.id);
      }
      final latest = ref
          .read(feedProvider)
          .posts
          .where((p) => p.id == post.id)
          .firstOrNull;
      ref
          .read(feedProvider.notifier)
          .updatePost((latest ?? post).copyWith(isSaved: next));
    } catch (_) {
      if (mounted) setState(() => _savedOverride = !next);
      messenger.showSnackBar(
        const SnackBar(content: Text('Bir hata oluştu.')),
      );
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  void _share() => SharePickerSheet.show(context, widget.post);

  @override
  Widget build(BuildContext context) {
    final post = widget.post;
    final card = widget.variant == PostCardVariant.compact
        ? _buildCompact(context)
        : _buildFeature(context);

    return RepaintBoundary(
      child: Semantics(
        label: 'Gönderi: ${post.title}',
        button: true,
        onTapHint: 'Detayları gör',
        onTap: widget.onTap,
        container: true,
        explicitChildNodes: true,
        child: card,
      ),
    );
  }

  // ── Feature variant ────────────────────────────────────────────────────

  Widget _buildFeature(BuildContext context) {
    final post = widget.post;
    final kc = context.kc;
    final isDesktop = context.isDesktop;
    final isPhoto = PostSurface.isPhotoPost(post);
    final isSensitive = post.isSensitive && !_revealSensitive;
    final railSize = isDesktop ? VoteRailSize.compact : VoteRailSize.card;
    final rail = VoteRailMetrics.of(railSize);
    // Text keeps a clear gap to the rail glass (desktop excerpts run 3
    // lines wide, so they get the same 20 px as the left padding).
    final rightPad = rail.width + (isDesktop ? 20 : 12);
    const headerHeight = 44.0;
    const headerTop = 12.0;

    return LayoutBuilder(
      builder: (context, constraints) {
        final width = constraints.maxWidth.isFinite
            ? constraints.maxWidth
            : MediaQuery.sizeOf(context).width;
        final minHeight = isPhoto
            ? (width * 1.18).clamp(440.0, 560.0)
            : (isDesktop ? 400.0 : 440.0);

        final bottomBlock = isSensitive
            ? _SensitiveVeil(
                onReveal: () => setState(() => _revealSensitive = true),
              )
            : _FeatureBottomBlock(
                post: post,
                isPhoto: isPhoto,
                isDesktop: isDesktop,
                searchQuery: widget.searchQuery,
              );

        Widget content = Stack(
          alignment: Alignment.bottomLeft,
          children: [
            // Tap layer: the first layer above the background, so the rail,
            // menu and tags stay separate targets.
            Positioned.fill(
              child: Material(
                type: MaterialType.transparency,
                child: InkWell(
                  onTap: widget.onTap,
                  excludeFromSemantics: true,
                  borderRadius: KararRadius.all(KararRadius.card),
                  splashColor: kc.splashOnColor,
                  highlightColor: kc.highlightOnColor,
                  hoverColor: kc.onColor.withValues(alpha: 0.04),
                  focusColor: Colors.transparent,
                ),
              ),
            ),
            SizedBox(height: minHeight, width: double.infinity),
            Padding(
              padding: EdgeInsets.fromLTRB(
                20,
                headerTop + headerHeight + 12,
                rightPad,
                18,
              ),
              child: bottomBlock,
            ),
            Positioned(
              top: headerTop,
              left: 20,
              // The "…" button stays just left of the rail's top curve.
              right: rail.width + (isDesktop ? 4 : 8),
              height: headerHeight,
              child: _FeatureHeader(
                post: post,
                onPhoto: isPhoto,
                showCategory: widget.showCategory,
                onDeleted: widget.onDeleted,
              ),
            ),
            Positioned(
              top: 28,
              right: 0,
              child: VoteRail(
                size: railSize,
                liveBlur: isPhoto,
                hakliCount: post.voteCountHakli,
                haksizCount: post.voteCountHaksiz,
                commentCount: post.commentCount,
                myVote: post.myVote,
                onVote: _railVote(post),
                onComment: widget.onTap,
                onSave: _toggleSave,
                isSaved: _isSaved,
                onShare: _share,
                pulseHakli: widget.showFirstVoteCoachmark,
              ),
            ),
            if (widget.showFirstVoteCoachmark)
              Positioned(
                right: rail.width + 12,
                top: 40,
                child: _FirstVoteCoachmark(
                  onDismiss: widget.onDismissFirstVoteCoachmark,
                ),
              ),
          ],
        );

        Widget card = PostSurface(
          post: post,
          seen: widget.isSeen,
          liveBlurHost: isPhoto,
          obscurePhoto: isPhoto && isSensitive,
          heroTag: isPhoto ? 'post_image_${post.id}_0' : null,
          child: content,
        );

        if (widget.isFocused) {
          card = CustomPaint(
            foregroundPainter: KararFocusRingPainter(
              color: kc.primary,
              radius: KararRadius.card,
            ),
            child: card,
          );
        }

        final reduce = KararMotion.reduce(context);
        return MouseRegion(
          onEnter: (_) => setState(() => _hovered = true),
          onExit: (_) => setState(() => _hovered = false),
          child: AnimatedSlide(
            offset: Offset(0, _hovered && !reduce ? -2 / minHeight : 0),
            duration: KararMotion.d(context, KararMotion.fast),
            curve: KararMotion.standard,
            child: card,
          ),
        );
      },
    );
  }

  /// Feed behaviour is unchanged: the rail calls `onVote` (tapping the same
  /// side again calls it again; no un-vote in the feed). Lists without a
  /// vote handler open the detail, where voting happens. Closed posts show
  /// the disabled rail.
  ValueChanged<VoteType>? _railVote(Post post) {
    if (widget.onVote != null) return widget.onVote;
    if (post.isClosed) return null;
    return (_) => widget.onTap();
  }

  // ── Compact variant ────────────────────────────────────────────────────

  Widget _buildCompact(BuildContext context) {
    final post = widget.post;
    final kc = context.kc;
    final style = CategoryStyle.of(post.category);
    final isSensitive = post.isSensitive && !_revealSensitive;
    final imageUrl = PostSurface.imageUrlOf(post);
    final query = widget.searchQuery ?? '';

    final titleBlock = isSensitive
        ? _CompactSensitive(
            onReveal: () => setState(() => _revealSensitive = true),
          )
        : Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              HighlightText(
                text: post.title,
                highlight: query,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: AppTypography.bodyStrong.copyWith(
                  fontSize: 16,
                  height: 1.3,
                  fontWeight: FontWeight.w800,
                  color: kc.text1,
                ),
                highlightStyle: TextStyle(
                  backgroundColor: kc.primaryContainer,
                  color: kc.onPrimaryContainer,
                  fontWeight: FontWeight.w800,
                ),
              ),
              const SizedBox(height: 4),
              Text(
                '${post.category.name} · ${post.createdAgo}',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: AppTypography.meta.copyWith(color: kc.text3),
              ),
              if (_showSnippet(post, query))
                _ContentSnippet(
                  content: post.content,
                  query: query,
                  style: AppTypography.bodyS.copyWith(color: kc.text2),
                  highlightStyle: TextStyle(
                    backgroundColor: kc.primaryContainer,
                    color: kc.onPrimaryContainer,
                    fontWeight: FontWeight.w700,
                  ),
                ),
            ],
          );

    Widget card = KararSurface(
      radius: KararRadius.surface,
      padding: const EdgeInsets.fromLTRB(16, 16, 6, 16),
      onTap: widget.onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _CategoryTile(style: style),
              const SizedBox(width: 12),
              Expanded(child: titleBlock),
              if (imageUrl != null && !isSensitive) ...[
                const SizedBox(width: 12),
                _PhotoThumb(url: imageUrl, style: style),
              ],
              _MoreMenu(
                post: post,
                onDeleted: widget.onDeleted,
                tone: _MoreMenuTone.surface,
              ),
            ],
          ),
          const SizedBox(height: 12),
          Padding(
            padding: const EdgeInsets.only(right: 10),
            child: VerdictChip(post: post, tone: VerdictChipTone.onSurface),
          ),
          const SizedBox(height: 8),
          Semantics(
            label: '${post.commentCount} yorum',
            excludeSemantics: true,
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
    );

    if (widget.isFocused) {
      card = CustomPaint(
        foregroundPainter: KararFocusRingPainter(
          color: kc.primary,
          radius: KararRadius.surface,
        ),
        child: card,
      );
    }
    return card;
  }
}

bool _showSnippet(Post post, String query) =>
    query.isNotEmpty &&
    !post.title.toLowerCase().contains(query.toLowerCase()) &&
    post.content.toLowerCase().contains(query.toLowerCase());

// ── Feature card parts ──────────────────────────────────────────────────────

/// Category chip (unless [showCategory] is off), optional second chip
/// (content source, else ranking label) and the "…" menu.
class _FeatureHeader extends StatelessWidget {
  const _FeatureHeader({
    required this.post,
    required this.onPhoto,
    this.showCategory = true,
    this.onDeleted,
  });

  final Post post;
  final bool onPhoto;
  final bool showCategory;
  final VoidCallback? onDeleted;

  @override
  Widget build(BuildContext context) {
    final style = CategoryStyle.of(post.category);
    final kc = context.kc;

    Widget? second;
    if (post.contentSource != 'user') {
      second = OnColorChip(
        label: post.contentSource == 'ai' ? 'AI destekli' : 'Sistem',
        dotColor: kc.gold,
        blur: onPhoto,
      );
    } else if (post.rankingLabel != null) {
      second = OnColorChip(
        label: post.rankingLabel!,
        icon: Icons.auto_awesome_rounded,
        blur: onPhoto,
      );
    }

    return Row(
      children: [
        Expanded(
          child: Align(
            alignment: Alignment.centerLeft,
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (showCategory)
                  Flexible(
                    child: OnColorChip(
                      icon: style.icon,
                      label: post.category.name,
                      blur: onPhoto,
                    ),
                  ),
                if (second != null) ...[
                  if (showCategory) const SizedBox(width: 6),
                  Flexible(child: second),
                ],
              ],
            ),
          ),
        ),
        const SizedBox(width: 4),
        _MoreMenu(
          post: post,
          onDeleted: onDeleted,
          tone: onPhoto ? _MoreMenuTone.photo : _MoreMenuTone.onColor,
        ),
      ],
    );
  }
}

/// Author row, title, excerpt, tags and the verdict chip (white on colour).
class _FeatureBottomBlock extends StatelessWidget {
  const _FeatureBottomBlock({
    required this.post,
    required this.isPhoto,
    required this.isDesktop,
    this.searchQuery,
  });

  final Post post;
  final bool isPhoto;
  final bool isDesktop;
  final String? searchQuery;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final query = searchQuery ?? '';
    // Search match on colour: a heavier weight + a 2× white underline (a
    // `backgroundColor` box is hard-edged and sits high on the line).
    final highlight = TextStyle(
      fontWeight: FontWeight.w900,
      decoration: TextDecoration.underline,
      decorationColor: kc.onColor,
      decorationThickness: 2,
    );
    final titleStyle =
        (isDesktop ? AppTypography.cardTitleDesktop : AppTypography.cardTitle)
            .copyWith(color: kc.onColor);
    final titleLines = isDesktop ? 3 : (isPhoto ? 3 : 4);
    final excerpt = post.content.trim();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        IgnorePointer(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              _AuthorRow(post: post),
              const SizedBox(height: 10),
              HighlightText(
                text: post.title,
                highlight: query,
                maxLines: titleLines,
                overflow: TextOverflow.ellipsis,
                style: titleStyle,
                highlightStyle: highlight,
              ),
              if (query.isEmpty && !isPhoto && excerpt.isNotEmpty) ...[
                const SizedBox(height: 8),
                Text(
                  excerpt,
                  maxLines: isDesktop ? 3 : 2,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.bodyS.copyWith(
                    color: kc.onColor,
                    height: 1.45,
                  ),
                ),
              ],
              if (_showSnippet(post, query))
                _ContentSnippet(
                  content: post.content,
                  query: query,
                  style: AppTypography.bodyS.copyWith(
                    color: kc.onColor,
                    height: 1.45,
                  ),
                  highlightStyle: highlight,
                ),
            ],
          ),
        ),
        if (post.tags.isNotEmpty)
          _TagRow(tags: post.tags)
        else
          const SizedBox(height: 12),
        IgnorePointer(child: VerdictChip(post: post)),
      ],
    );
  }
}

class _AuthorRow extends StatelessWidget {
  const _AuthorRow({required this.post});

  final Post post;

  @override
  Widget build(BuildContext context) {
    final onColor = context.kc.onColor;
    final handle =
        post.isAnonymous ? '@anonim' : '@${post.authorName ?? 'misafir'}';
    return Row(
      children: [
        KararAvatar(
          username: post.authorName ?? 'Misafir',
          radius: 14,
          fontSize: 12,
          anonymous: post.isAnonymous,
          onColor: true,
        ),
        const SizedBox(width: 8),
        Expanded(
          child: Text.rich(
            TextSpan(
              children: [
                TextSpan(
                  text: handle,
                  style: AppTypography.bodyS.copyWith(
                    fontWeight: FontWeight.w700,
                    color: onColor,
                  ),
                ),
                TextSpan(
                  text: ' · ${post.createdAgo}',
                  style: AppTypography.labelS.copyWith(
                    fontWeight: FontWeight.w500,
                    color: onColor,
                  ),
                ),
              ],
            ),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }
}

/// Up to three `#tag` buttons on one line (never wrapping, never centred);
/// tags that do not fit are dropped. Each tag keeps a ≥ 44 × 44 hit area.
class _TagRow extends StatelessWidget {
  const _TagRow({required this.tags});

  final List<String> tags;

  static const _gap = 10.0;

  @override
  Widget build(BuildContext context) {
    final style = AppTypography.labelS.copyWith(
      fontWeight: FontWeight.w800,
      color: context.kc.onColor,
    );
    return LayoutBuilder(
      builder: (context, constraints) {
        final scaler = MediaQuery.textScalerOf(context);
        final direction = Directionality.of(context);
        final maxWidth = constraints.maxWidth;
        final visible = <(String, double)>[];
        var used = 0.0;
        for (final tag in tags.take(3)) {
          final painter = TextPainter(
            text: TextSpan(text: '#$tag', style: style),
            textDirection: direction,
            textScaler: scaler,
            maxLines: 1,
          )..layout();
          final width = math.max(painter.width.ceilToDouble(), 44.0);
          painter.dispose();
          final needed = used + (visible.isEmpty ? 0 : _gap) + width;
          if (needed > maxWidth) {
            if (visible.isEmpty) visible.add((tag, maxWidth));
            break;
          }
          visible.add((tag, width));
          used = needed;
        }

        return SizedBox(
          height: 44,
          child: Row(
            children: [
              for (var i = 0; i < visible.length; i++) ...[
                if (i > 0) const SizedBox(width: _gap),
                KararPressable(
                  onTap: () => context.push(
                    '/search?q=${Uri.encodeComponent('#${visible[i].$1}')}',
                  ),
                  semanticLabel: '#${visible[i].$1}',
                  pressedScale: 0.96,
                  focusRadius: 6,
                  child: SizedBox(
                    width: visible[i].$2,
                    child: Text(
                      '#${visible[i].$1}',
                      maxLines: 1,
                      softWrap: false,
                      overflow: TextOverflow.ellipsis,
                      style: style,
                    ),
                  ),
                ),
              ],
            ],
          ),
        );
      },
    );
  }
}

/// Sensitive-content veil: replaces the bottom block (the rail stays).
class _SensitiveVeil extends StatelessWidget {
  const _SensitiveVeil({required this.onReveal});

  final VoidCallback onReveal;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: kc.photoVeil,
        borderRadius: KararRadius.all(KararRadius.tile),
      ),
      child: Row(
        children: [
          ExcludeSemantics(
            child: Icon(
              Icons.warning_amber_rounded,
              size: 22,
              color: kc.gold,
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              'Hassas İçerik',
              style: AppTypography.bodyStrong.copyWith(
                fontWeight: FontWeight.w800,
                color: kc.onColor,
              ),
            ),
          ),
          const SizedBox(width: 8),
          KararPressable(
            onTap: onReveal,
            semanticLabel: 'GÖSTER',
            focusRadius: 18,
            child: Container(
              constraints: const BoxConstraints(minHeight: 36),
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: kc.capsule,
                borderRadius: KararRadius.all(KararRadius.pill),
              ),
              child: Text(
                'GÖSTER',
                style: AppTypography.labelS.copyWith(
                  fontWeight: FontWeight.w800,
                  color: kc.onCapsuleText,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// "İlk oyunu at" bubble pointing at the Haklı capsule.
class _FirstVoteCoachmark extends StatelessWidget {
  const _FirstVoteCoachmark({this.onDismiss});

  final VoidCallback? onDismiss;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final brightness = Theme.of(context).brightness;
    return Semantics(
      label: 'İlk oyunu at ipucu',
      container: true,
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 220),
            child: Container(
              padding: const EdgeInsets.fromLTRB(12, 12, 4, 12),
              decoration: BoxDecoration(
                color: kc.ink,
                borderRadius: KararRadius.all(KararRadius.input),
                boxShadow: KararShadow.float(brightness),
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Flexible(
                    child: Padding(
                      padding: const EdgeInsets.only(top: 2),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            'İlk oyunu at',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: AppTypography.bodyStrong.copyWith(
                              fontWeight: FontWeight.w800,
                              color: kc.onInk,
                            ),
                          ),
                          const SizedBox(height: 2),
                          Text(
                            'Haklı ya da Haksız seç.',
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: AppTypography.labelS.copyWith(
                              fontWeight: FontWeight.w500,
                              color: kc.onInk,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                  IconButton(
                    key: const ValueKey('first_vote_coachmark_dismiss'),
                    tooltip: 'Kapat',
                    constraints: const BoxConstraints(
                      minWidth: 44,
                      minHeight: 44,
                    ),
                    padding: EdgeInsets.zero,
                    onPressed: onDismiss,
                    icon: const Icon(Icons.close_rounded, size: 18),
                    color: kc.onInk,
                  ),
                ],
              ),
            ),
          ),
          ExcludeSemantics(
            child: CustomPaint(
              size: const Size(10, 18),
              painter: _ArrowPainter(kc.ink),
            ),
          ),
        ],
      ),
    );
  }
}

class _ArrowPainter extends CustomPainter {
  const _ArrowPainter(this.color);

  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final path = Path()
      ..moveTo(0, 0)
      ..lineTo(size.width, size.height / 2)
      ..lineTo(0, size.height)
      ..close();
    canvas.drawPath(path, Paint()..color = color);
  }

  @override
  bool shouldRepaint(_ArrowPainter oldDelegate) => oldDelegate.color != color;
}

// ── Compact card parts ──────────────────────────────────────────────────────

/// 44 px rounded-square category tile (gradient + white icon).
class _CategoryTile extends StatelessWidget {
  const _CategoryTile({required this.style});

  final CategoryStyle style;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return ExcludeSemantics(
      child: Container(
        width: 44,
        height: 44,
        decoration: BoxDecoration(
          gradient: style.gradientFor(brightness: kc.brightness),
          borderRadius: KararRadius.all(KararRadius.iconTile),
        ),
        alignment: Alignment.center,
        child: Icon(style.icon, size: 22, color: kc.onColor),
      ),
    );
  }
}

/// 64 px photo thumbnail (radius 16); the category gradient shows while it
/// loads and if it fails.
class _PhotoThumb extends StatelessWidget {
  const _PhotoThumb({required this.url, required this.style});

  final String url;
  final CategoryStyle style;

  @override
  Widget build(BuildContext context) {
    final fallback = DecoratedBox(
      decoration: BoxDecoration(
        gradient: style.gradientFor(brightness: context.kc.brightness),
      ),
    );
    final dpr = MediaQuery.devicePixelRatioOf(context);
    return ExcludeSemantics(
      child: ClipRRect(
        borderRadius: KararRadius.all(KararRadius.input),
        child: SizedBox(
          width: 64,
          height: 64,
          child: CachedNetworkImage(
            imageUrl: url,
            fit: BoxFit.cover,
            memCacheWidth: (64 * dpr).round(),
            fadeInDuration: const Duration(milliseconds: 180),
            placeholder: (_, __) => fallback,
            errorWidget: (_, __, ___) => fallback,
          ),
        ),
      ),
    );
  }
}

class _CompactSensitive extends StatelessWidget {
  const _CompactSensitive({required this.onReveal});

  final VoidCallback onReveal;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Row(
      children: [
        ExcludeSemantics(
          child: Icon(
            Icons.warning_amber_rounded,
            size: 20,
            color: kc.warnText,
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: Text(
            'Hassas İçerik',
            style: AppTypography.bodyStrong.copyWith(
              fontWeight: FontWeight.w800,
              color: kc.text1,
            ),
          ),
        ),
        TextButton(
          onPressed: onReveal,
          child: const Text('GÖSTER'),
        ),
      ],
    );
  }
}

class _ContentSnippet extends StatelessWidget {
  const _ContentSnippet({
    required this.content,
    required this.query,
    required this.style,
    required this.highlightStyle,
  });

  final String content;
  final String query;
  final TextStyle style;
  final TextStyle highlightStyle;

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
      padding: const EdgeInsets.only(top: 6),
      child: HighlightText(
        text: snippet,
        highlight: query,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
        style: style,
        highlightStyle: highlightStyle,
      ),
    );
  }
}

// ── "…" menu ────────────────────────────────────────────────────────────────

enum _MoreMenuTone { onColor, photo, surface }

class _MoreMenu extends ConsumerWidget {
  const _MoreMenu({
    required this.post,
    this.onDeleted,
    this.tone = _MoreMenuTone.onColor,
  });

  final Post post;
  final VoidCallback? onDeleted;
  final _MoreMenuTone tone;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final kc = context.kc;
    return Builder(
      builder: (buttonContext) => KararCircleButton(
        icon: Icons.more_horiz_rounded,
        tooltip: 'Gönderi seçenekleri',
        size: 36,
        iconSize: 22,
        style: switch (tone) {
          _MoreMenuTone.onColor => KararCircleStyle.onColor,
          _MoreMenuTone.photo => KararCircleStyle.glass,
          _MoreMenuTone.surface => KararCircleStyle.muted,
        },
        backgroundColor:
            tone == _MoreMenuTone.surface ? Colors.transparent : null,
        iconColor: tone == _MoreMenuTone.surface ? kc.text2 : null,
        onPressed: () => _open(buttonContext, ref),
      ),
    );
  }

  Future<void> _open(BuildContext context, WidgetRef ref) async {
    final button = context.findRenderObject() as RenderBox?;
    final overlay =
        Navigator.of(context).overlay?.context.findRenderObject() as RenderBox?;
    if (button == null || overlay == null) return;
    final position = RelativeRect.fromRect(
      Rect.fromPoints(
        button.localToGlobal(Offset.zero, ancestor: overlay),
        button.localToGlobal(
          button.size.bottomRight(Offset.zero),
          ancestor: overlay,
        ),
      ),
      Offset.zero & overlay.size,
    );
    final value = await showMenu<String>(
      context: context,
      position: position,
      items: _items(context),
    );
    if (value == null || !context.mounted) return;
    _handleAction(context, ref, value);
  }

  List<PopupMenuEntry<String>> _items(BuildContext context) {
    final kc = context.kc;
    Widget row(IconData icon, String label, {bool destructive = false}) {
      final color = destructive ? kc.haksizText : null;
      return Row(
        children: [
          Icon(icon, size: 20, color: color ?? kc.text2),
          const SizedBox(width: 12),
          Text(label, style: color == null ? null : TextStyle(color: color)),
        ],
      );
    }

    return [
      PopupMenuItem(
        value: 'azalt',
        child: row(Icons.do_not_disturb_on_outlined, 'Bunu azalt'),
      ),
      PopupMenuItem(
        value: 'report',
        child: row(Icons.flag_outlined, 'Şikayet Et'),
      ),
      if (post.isOwner)
        PopupMenuItem(
          value: 'delete',
          child: row(Icons.delete_outline_rounded, 'Sil', destructive: true),
        ),
      if (post.authorId != null && !post.isOwner)
        PopupMenuItem(
          value: 'block',
          child: row(
            Icons.block_rounded,
            'Kullanıcıyı Engelle',
            destructive: true,
          ),
        ),
    ];
  }

  void _handleAction(BuildContext context, WidgetRef ref, String action) {
    if (action == 'azalt') {
      _showFeedbackReasonSheet(context, ref);
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

  Future<void> _showFeedbackReasonSheet(
      BuildContext context, WidgetRef ref) async {
    final messenger = ScaffoldMessenger.of(context);
    final reason = await FeedbackReasonBottomSheet.show(context);
    if (reason == null) return;
    _markNotInterested(messenger, ref, reason);
  }

  void _markNotInterested(
      ScaffoldMessengerState messenger, WidgetRef ref, String reason) {
    final feedNotifier = ref.read(feedProvider.notifier);
    final postIndex =
        ref.read(feedProvider).posts.indexWhere((p) => p.id == post.id);
    feedNotifier.markNotInterested(post.id, reason: reason);

    messenger.showSnackBar(
      SnackBar(
        content: const Text('Artık bu gönderiyi görmeyeceksin.'),
        action: SnackBarAction(
          label: 'Geri Al',
          onPressed: () {
            feedNotifier.undoRemove(post, postIndex == -1 ? null : postIndex);
          },
        ),
      ),
    );
  }

  ButtonStyle _destructiveStyle(BuildContext context) =>
      FilledButton.styleFrom(
        backgroundColor: context.kc.haksizFill,
        foregroundColor: context.kc.onColor,
      );

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
            style: _destructiveStyle(ctx),
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
            style: _destructiveStyle(ctx),
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
